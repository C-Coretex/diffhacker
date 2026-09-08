namespace DiffHacker.Core.Tools;

/// <summary>
/// Forwards every progress report onward and keeps a copy.
/// <para>
/// The live view wants them as they happen and the stored trace wants them afterwards, and neither
/// is worth a second mechanism. Nothing here may throw: the contract on
/// <see cref="IToolProgressSink"/> is that a report can never fail a run.
/// </para>
/// </summary>
public sealed class RecordingProgressSink(IToolProgressSink inner) : IToolProgressSink
{
    private readonly Lock _gate = new();
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    public async ValueTask ReportAsync(ToolProgressReport report, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _messages.Add(report.Message);
        }

        await inner.ReportAsync(report, cancellationToken).ConfigureAwait(false);
    }
}
