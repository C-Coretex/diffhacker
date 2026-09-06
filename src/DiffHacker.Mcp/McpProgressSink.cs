using System.Globalization;
using DiffHacker.Core.Tools;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Mcp;

/// <summary>
/// Where <c>report_progress</c> goes when the toolbox is served over stdio.
/// <para>
/// In the application the matching sink pushes a JSON-RPC notification into the WebView, so a
/// reviewer watching a run sees what the model is doing. Over stdio there is no window: the
/// audience is the agent that connected, and its own console is where its user is already
/// looking.
/// </para>
/// <para>
/// This writes to the log, which on this server means stderr. The obvious alternative —
/// <c>notifications/message</c>, MCP's own logging channel — was deprecated in specification
/// version 2026-07-28 (SEP-2577) and the SDK errors on it, so stderr is now the sanctioned
/// place for a server to say what it is doing. stdout is untouchable here: it carries the
/// protocol, and one stray line on it hangs the client.
/// </para>
/// </summary>
internal sealed partial class McpProgressSink(ILogger<McpProgressSink> logger) : IToolProgressSink
{
    public ValueTask ReportAsync(ToolProgressReport report, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var phase = report.Phase is null ? string.Empty : $"[{report.Phase}] ";

        ProgressReported(
            logger,
            report.Sequence,
            string.Create(CultureInfo.InvariantCulture, $"{phase}{report.Message}"));

        return ValueTask.CompletedTask;
    }

    [LoggerMessage(EventId = 5020, Level = LogLevel.Information, Message = "Progress #{Sequence}: {Text}")]
    private static partial void ProgressReported(ILogger logger, int sequence, string text);
}
