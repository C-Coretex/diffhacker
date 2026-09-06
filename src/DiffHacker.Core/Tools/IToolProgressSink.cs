namespace DiffHacker.Core.Tools;

/// <summary>
/// Where <c>report_progress</c> goes.
/// <para>
/// The toolbox cannot reference <c>DiffHacker.Host</c> (§0.3), and the standalone MCP server has
/// no host to reference, so the two destinations — a JSON-RPC notification into the WebView, and
/// an MCP logging notification to whichever agent connected over stdio — meet at this interface
/// rather than inside the tool.
/// </para>
/// <para>
/// Reporting progress must never be able to fail a run: an implementation that cannot deliver
/// drops the report and says so in the log. The tool tells the model "recorded" either way,
/// because whether a notification reached a window is not something the model can act on.
/// </para>
/// </summary>
public interface IToolProgressSink
{
    ValueTask ReportAsync(ToolProgressReport report, CancellationToken cancellationToken);
}

/// <summary>
/// One announcement from the model about where it has got to.
/// </summary>
/// <param name="Sequence">
/// One-based and monotonic within a session. Notifications have no delivery ordering guarantee
/// worth relying on, so the receiver needs a way to drop a stale one rather than let progress
/// appear to run backwards.
/// </param>
/// <param name="Message">
/// The model's own words, shown to the reviewer as-is. This is the one string in the application
/// the host does not author (§0.6) — it is data produced during the run, not UI copy.
/// </param>
/// <param name="Phase">
/// Optional coarse stage. A stable key the renderer can translate; unknown values render as the
/// message alone rather than as a missing string.
/// </param>
/// <param name="At">When the report was made, UTC.</param>
public readonly record struct ToolProgressReport(
    int Sequence,
    string Message,
    string? Phase,
    DateTimeOffset At);
