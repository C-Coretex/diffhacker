using System.Reflection;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Repositories;
using DiffHacker.Core.Settings;
using DiffHacker.Git;
using DiffHacker.Host.Assets;
using DiffHacker.Host.Logging;
using DiffHacker.Host.Rpc;
using DiffHacker.Host.Shell;
using DiffHacker.Llm;
using DiffHacker.Storage;
using DiffHacker.Storage.Secrets;
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

        // --data-dir exists so the end-to-end suite can run against throwaway state: it writes
        // provider profiles and API keys, and must never touch the developer's real ones.
        var paths = ResolvePaths(options);

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

        // Blocks on the native message loop until the window closes.
        shell.Run(UiAssetResolver.StartUrl);

        provider.DisposeAsync().AsTask().GetAwaiter().GetResult();

        logger.LogInformation("DiffHacker exiting");
        return 0;
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
        services.AddSingleton(new HostRuntimeInfo());

        services.AddSingleton(CreateWindowSettings());
        services.AddSingleton<IAppShell, PhotinoAppShell>();
        services.AddSingleton(CreateAssetSource());
        services.AddSingleton<UiAssetResolver>();

        // Git: an allowlisted process runner, the two read-only questions Iteration 2 asks of it,
        // and the changeset everything downstream is built from.
        services.AddSingleton<GitProcessRunner>();
        services.AddSingleton<IGitEnvironment, GitEnvironment>();
        services.AddSingleton<IRepositoryLocator, RepositoryLocator>();
        services.AddSingleton<IGitClient, GitClient>();

        // Storage: settings in the per-user data directory, keys in the secret store, never
        // the other way round.
        services.AddSingleton(sp => new AppDatabase(paths.DatabaseFile, sp.GetRequiredService<ILogger<AppDatabase>>()));
        services.AddSingleton<IRecentRepositoryStore, SqliteRecentRepositoryStore>();
        services.AddSingleton<IProviderProfileStore, SqliteProviderProfileStore>();
        services.AddSingleton(sp => SecretStoreFactory.Create(
            paths.SecretsFile,
            paths.MasterKeyFile,
            paths.SecretSaltFile,
            sp.GetRequiredService<ILogger<AppPaths>>()));

        // One HttpClient for the lifetime of the process. Connection tests are rare and
        // user-initiated, so there is nothing here for IHttpClientFactory to solve.
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton<IProviderConnectionTester, HttpProviderConnectionTester>();

        // The notifier is the bridge's outbound-notification plumbing. Nothing pushes
        // notifications yet — Iteration 5's report_progress is the first real caller.
        services.AddSingleton<RpcNotifier>();
        services.AddSingleton<IRpcNotifier>(sp => sp.GetRequiredService<RpcNotifier>());
        services.AddSingleton<HostRpcTarget>();
        services.AddSingleton<EnvironmentRpcTarget>();
        services.AddSingleton<RepositoryRpcTarget>();
        services.AddSingleton<ProviderRpcTarget>();
        services.AddSingleton<ChangesetRpcTarget>();

        services.AddSingleton(sp => new RpcBridge(
            sp.GetRequiredService<IAppShell>(),
            sp.GetRequiredService<RpcNotifier>(),
            [
                sp.GetRequiredService<HostRpcTarget>(),
                sp.GetRequiredService<EnvironmentRpcTarget>(),
                sp.GetRequiredService<RepositoryRpcTarget>(),
                sp.GetRequiredService<ProviderRpcTarget>(),
                sp.GetRequiredService<ChangesetRpcTarget>(),
            ],
            sp.GetRequiredService<ILogger<RpcBridge>>()));

        return services;
    }

    /// <summary>
    /// Where this run keeps its state: an explicit <c>--data-dir</c>, or the per-user
    /// application data directory.
    /// </summary>
    internal static AppPaths ResolvePaths(HostCommandLine options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.DataDirectory)
            ? new AppPaths()
            : new AppPaths(options.DataDirectory);
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
