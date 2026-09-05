using System.Reflection;
using DiffHacker.Host.Assets;
using DiffHacker.Host.Logging;
using DiffHacker.Host.Rpc;
using DiffHacker.Host.SelfTest;
using DiffHacker.Host.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace DiffHacker.Host;

/// <summary>
/// Composition root. Builds the object graph, opens the window, and blocks on the native
/// message loop until it closes.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Single-threaded apartment: WebView2 on Windows requires the window to live on an STA
    /// thread, and both WKWebView and WebKitGTK require it to be the process main thread.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var options = HostCommandLine.Parse(args);
        var paths = new AppPaths();

        using var serilog = LoggingSetup.Create(paths, options.Verbose);
        using var loggerFactory = new SerilogLoggerFactory(serilog);
        var logger = loggerFactory.CreateLogger(typeof(Program));

        try
        {
            return Run(options, paths, loggerFactory, logger);
        }
#pragma warning disable CA1031 // Top-level handler: anything reaching here must land in log.txt.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogCritical(ex, "DiffHacker terminated unexpectedly");
            return 70; // EX_SOFTWARE
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static int Run(
        HostCommandLine options,
        AppPaths paths,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        var services = BuildServices(options, paths, loggerFactory);

        // ServiceProvider.Dispose throws for services that are IAsyncDisposable only, and
        // RpcBridge is one, so the container is torn down asynchronously.
        var provider = services.BuildServiceProvider();

        var shell = provider.GetRequiredService<IAppShell>();
        var resolver = provider.GetRequiredService<UiAssetResolver>();
        var assetSource = provider.GetRequiredService<IAssetSource>();

        logger.LogInformation(
            "DiffHacker {Version} starting; contract {Contract}; data {DataDirectory}; assets from {AssetSource}",
            provider.GetRequiredService<HostRuntimeInfo>().AppVersion,
            Contracts.ContractVersion.Current,
            paths.DataDirectory,
            assetSource.Description);

        // No HTTP server and no localhost port: the renderer is served in-process, which is
        // what makes the WebView's CSP meaningful.
        shell.RegisterAssetScheme(UiAssetResolver.Scheme, resolver.Resolve);

        var bridge = provider.GetRequiredService<RpcBridge>();
        bridge.Start();

        var exitCode = 0;
        using var shutdown = new CancellationTokenSource();

        if (options.SelfTest)
        {
            _ = Task.Run(async () =>
            {
                exitCode = await RunSelfTestAsync(provider, options, logger, shutdown.Token).ConfigureAwait(false);
                shell.Close();
            }, shutdown.Token);
        }

        // Blocks on the native message loop until the window closes.
        shell.Run(UiAssetResolver.StartUrl);

        shutdown.Cancel();
        provider.DisposeAsync().AsTask().GetAwaiter().GetResult();

        logger.LogInformation("DiffHacker exiting with code {ExitCode}", exitCode);
        return exitCode;
    }

    private static async Task<int> RunSelfTestAsync(
        IServiceProvider provider,
        HostCommandLine options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var coordinator = provider.GetRequiredService<SelfTestCoordinator>();

        var result = await coordinator.WaitAsync(options.SelfTestTimeout, cancellationToken).ConfigureAwait(false);
        await SelfTestCoordinator
            .WriteResultAsync(options.SelfTestOutputPath, result, CancellationToken.None)
            .ConfigureAwait(false);

        var succeeded = result?.Succeeded == true;
        logger.LogInformation(
            "Self-test {Outcome}; result written to {Path}",
            succeeded ? "passed" : "FAILED",
            Path.GetFullPath(options.SelfTestOutputPath));

        return succeeded ? 0 : 1;
    }

    private static ServiceCollection BuildServices(
        HostCommandLine options,
        AppPaths paths,
        ILoggerFactory loggerFactory)
    {
        var services = new ServiceCollection();

        services.AddSingleton(loggerFactory);
        services.AddLogging();
        services.AddSingleton(paths);
        services.AddSingleton(options);
        services.AddSingleton(new HostRuntimeInfo { SelfTest = options.SelfTest });

        services.AddSingleton(CreateWindowSettings());
        services.AddSingleton<IAppShell, PhotinoAppShell>();
        services.AddSingleton(CreateAssetSource());
        services.AddSingleton<UiAssetResolver>();

        services.AddSingleton<SelfTestCoordinator>();
        services.AddSingleton<RpcNotifier>();
        services.AddSingleton<IRpcNotifier>(sp => sp.GetRequiredService<RpcNotifier>());
        services.AddSingleton<HostRpcTarget>();
        services.AddSingleton<DemoRpcTarget>();

        services.AddSingleton(sp => new RpcBridge(
            sp.GetRequiredService<IAppShell>(),
            sp.GetRequiredService<RpcNotifier>(),
            [sp.GetRequiredService<HostRpcTarget>(), sp.GetRequiredService<DemoRpcTarget>()],
            sp.GetRequiredService<ILogger<RpcBridge>>()));

        return services;
    }

    private static WindowSettings CreateWindowSettings() => new()
    {
#if DEBUG
        DevToolsEnabled = true,
#else
        DevToolsEnabled = false,
#endif
    };

    /// <summary>
    /// Release builds serve the renderer from resources embedded in this assembly. Debug
    /// builds read <c>src/ui/dist</c> from disk, so <c>vite build --watch</c> plus a window
    /// reload is the inner loop.
    /// </summary>
#pragma warning disable CA1859 // The interface is the point: the concrete type varies by configuration.
    private static IAssetSource CreateAssetSource()
#pragma warning restore CA1859
    {
#if DEBUG
        var distPath = typeof(Program).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "DiffHacker.UiDistPath")?.Value;

        if (!string.IsNullOrWhiteSpace(distPath) && Directory.Exists(distPath))
        {
            return new DirectoryAssetSource(distPath);
        }
#endif
        return new EmbeddedAssetSource(typeof(Program).Assembly);
    }
}
