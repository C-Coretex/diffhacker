using Serilog;
using Serilog.Formatting.Display;

namespace DiffHacker.Host.Logging;

/// <summary>
/// Builds the application logger: a rolling <c>log.txt</c> in the per-user application data
/// directory, with structured properties on every line and credentials removed at the sink.
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Readable for a support request and structured for a machine: an ISO-8601 timestamp,
    /// the level, the source type, the rendered message, then the event's properties as JSON.
    /// </summary>
    private const string OutputTemplate =
        "{Timestamp:O} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}";

    private const long FileSizeLimitBytes = 32L * 1024 * 1024;
    private const int RetainedFileCount = 14;

    public static Serilog.Core.Logger Create(AppPaths paths, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureCreated();

        var formatter = new RedactingTextFormatter(
            new MessageTemplateTextFormatter(OutputTemplate, formatProvider: null));

        var configuration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.File(
                formatter,
                paths.LogFile,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: FileSizeLimitBytes,
                retainedFileCountLimit: RetainedFileCount,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(2));

        _ = verbose
            ? configuration.MinimumLevel.Debug()
            : configuration.MinimumLevel.Information();

        return configuration.CreateLogger();
    }
}
