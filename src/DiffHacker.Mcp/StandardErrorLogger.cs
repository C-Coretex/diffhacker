using System.Globalization;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Mcp;

/// <summary>
/// Logs to stderr.
/// <para>
/// stdout carries the MCP protocol and nothing else may touch it — a stray line there is a
/// protocol violation that presents as the client hanging. stderr is where MCP servers are meant
/// to put diagnostics, and clients surface it, so requirement 5's "log every tool call and its
/// result size" is satisfied by default here rather than only when someone passes a flag.
/// </para>
/// <para>
/// Hand-written because the console sink packages are not on the approved list and this is forty
/// lines. Nothing sensitive passes through this process — the MCP server holds no API keys and
/// talks to no provider — so unlike the application's logger it needs no redaction.
/// </para>
/// </summary>
internal sealed class StandardErrorLoggerProvider(LogLevel minimum) : ILoggerProvider
{
    private static readonly Lock Gate = new();

    public ILogger CreateLogger(string categoryName) => new StandardErrorLogger(categoryName, minimum);

    public void Dispose()
    {
        // Nothing owned: Console.Error belongs to the process.
    }

    private sealed class StandardErrorLogger(string category, LogLevel minimum) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);

            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow:HH:mm:ss} [{Abbreviate(logLevel)}] {category}: {formatter(state, exception)}");

            // One lock: tools run concurrently within a turn, and interleaved half-lines would
            // make the log worse than no log.
            lock (Gate)
            {
                Console.Error.WriteLine(line);

                if (exception is not null)
                {
                    Console.Error.WriteLine(exception);
                }
            }
        }

        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Trace => "trc",
            LogLevel.Debug => "dbg",
            LogLevel.Information => "inf",
            LogLevel.Warning => "wrn",
            LogLevel.Error => "err",
            LogLevel.Critical => "crt",
            _ => "?",
        };
    }
}
