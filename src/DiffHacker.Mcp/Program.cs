using DiffHacker.Core.Changes;
using DiffHacker.Git;
using DiffHacker.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Extensions.Logging;

namespace DiffHacker.Mcp;

/// <summary>
/// The standalone MCP server: DiffHacker's repository toolbox, headless, on stdio.
/// <para>
/// Composed the way <c>DiffHacker.Host</c> composes — a plain <c>ServiceCollection</c> is not
/// even needed here, because the dependency graph is four objects deep and writing it out is
/// shorter and clearer than registering it.
/// </para>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = McpCommandLine.Parse(args);

        if (options.Help)
        {
            Console.Error.WriteLine(McpCommandLine.Usage);
            return 0;
        }

        if (options.Error is { } error)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(McpCommandLine.Usage);
            return 64; // EX_USAGE
        }

        using var loggerFactory = CreateLoggerFactory(options);

        try
        {
            return await RunAsync(options, loggerFactory).ConfigureAwait(false);
        }
        catch (GitClientException ex)
        {
            // The overwhelmingly likely failure: --repository does not point at a working tree.
            // Said plainly on stderr, because the person who wrote the client configuration is
            // the one who has to fix it.
            Console.Error.WriteLine(ex.Message);
            return 66; // EX_NOINPUT
        }
        catch (OperationCanceledException)
        {
            // The client closed the transport. An ordinary shutdown.
            return 0;
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("DiffHacker.Mcp").LogCritical(ex, "The MCP server stopped.");
            return 70; // EX_SOFTWARE
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    private static async Task<int> RunAsync(McpCommandLine options, ILoggerFactory loggerFactory)
    {
        var runner = new GitProcessRunner(loggerFactory.CreateLogger<GitProcessRunner>());
        var environment = new GitEnvironment(runner, loggerFactory.CreateLogger<GitEnvironment>());
        var git = new GitClient(runner, environment, loggerFactory.CreateLogger<GitClient>());

        var progress = new McpProgressSink(loggerFactory.CreateLogger<McpProgressSink>());

        // The toolbox is opened before the server exists, because the tools are what the server
        // is built from. Opening it here also means a bad --repository fails now, with a message,
        // rather than on the client's first tool call.
        var catalogue = await Toolbox.OpenAsync(
            new ToolboxOptions
            {
                Git = git,
                LoggerFactory = loggerFactory,
                Progress = progress,
            },
            options.Repository!,
            CancellationToken.None).ConfigureAwait(false);

        await using var transport = new StdioServerTransport("diffhacker", loggerFactory);

        await using var server = McpServer.Create(
            transport,
            new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "diffhacker", Version = Version() },
                ServerInstructions = Instructions,
                ToolCollection = [.. catalogue.McpTools],
            },
            loggerFactory);

        using var stopping = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };

        await server.RunAsync(stopping.Token).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// What the client is told about the server before it calls anything.
    /// <para>
    /// Prompt text, like the tool descriptions. It exists to stop the two mistakes an agent makes
    /// first: reading files one at a time when it could search, and assuming an ignored path does
    /// not exist.
    /// </para>
    /// </summary>
    private const string Instructions = """
        DiffHacker's repository toolbox. Read-only: nothing here writes to the repository, runs a
        command, or reaches the network.

        The repository is fixed when the server starts — no tool takes a repository path.

        A good order to work in: get_project_profile, then get_repository_tree for the layout,
        then list_changed_files for the change under review. Use search_text to find things and
        read_file to read around what you find; prefer one search over many speculative reads.

        Only files git can see are visible. Anything covered by .gitignore — node_modules, build
        output — cannot be read by any tool here. If a path you expect is missing, get_path_info
        will tell you whether it is absent or merely ignored.

        Results are capped and paged. A truncated result always states the true total and gives a
        cursor for the next page.
        """;

    private static string Version() =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static ILoggerFactory CreateLoggerFactory(McpCommandLine options)
    {
        var level = options.Verbose ? LogLevel.Debug : LogLevel.Information;

        var factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(level);
            builder.AddProvider(new StandardErrorLoggerProvider(level));

            if (options.LogFile is not { } path)
            {
                return;
            }

            var serilog = new LoggerConfiguration()
                .MinimumLevel.Is(options.Verbose
                    ? Serilog.Events.LogEventLevel.Debug
                    : Serilog.Events.LogEventLevel.Information)
                .WriteTo.File(
                    path,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 32L * 1024 * 1024,
                    retainedFileCountLimit: 7,
                    flushToDiskInterval: TimeSpan.FromSeconds(2))
                .CreateLogger();

            Log.Logger = serilog;
            builder.AddProvider(new SerilogLoggerProvider(serilog, dispose: true));
        });

        return factory;
    }
}
