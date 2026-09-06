using DiffHacker.Contracts;
using DiffHacker.Core.Tools;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Carries <c>report_progress</c> from the toolbox to the renderer as an
/// <c>analysis.progress</c> notification.
/// <para>
/// This is the first real producer on the host-to-renderer notification channel, which existed
/// unused from Iteration 1. It is named for the analysis run rather than for the tool because
/// Iteration 7's pipeline is what will render it and <c>report_progress</c> is merely its first
/// source — naming it <c>tools.progress</c> would mean renaming it later for no gain.
/// </para>
/// <para>
/// The message is the one user-facing string in the application the host does not author. §0.6
/// bans host-authored prose because the renderer owns the resource layer; this is not prose, it
/// is data the model produced during the run, and translating it would be nonsense. The
/// <c>phase</c> beside it <i>is</i> a key, and the renderer translates that.
/// </para>
/// </summary>
public sealed class ToolProgressNotifier(IRpcNotifier notifier) : IToolProgressSink
{
    /// <summary>The notification method name. Mirrored in the renderer's RpcNotifications.</summary>
    public const string Method = "analysis.progress";

    public async ValueTask ReportAsync(ToolProgressReport report, CancellationToken cancellationToken)
    {
        var payload = new AnalysisProgress(
            report.At.UtcDateTime,
            report.Message,
            ToPhase(report.Phase),
            report.Sequence);

        // RpcNotifier already swallows a disposed or lost connection and logs it, so a reviewer
        // closing the window mid-run cannot fail the model's tool call.
        await notifier.NotifyAsync(Method, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps the model's phase onto the contract's enum, dropping anything it invented.
    /// <para>
    /// The model is told which phases exist but is not bound by it. An unknown value becomes no
    /// phase at all rather than a wire value the renderer has no string for, which would reach a
    /// reader as a raw identifier.
    /// </para>
    /// </summary>
    private static AnalysisProgressPhase? ToPhase(string? phase) => phase switch
    {
        "exploring" => AnalysisProgressPhase.Exploring,
        "analysing" => AnalysisProgressPhase.Analysing,
        "grouping" => AnalysisProgressPhase.Grouping,
        "explaining" => AnalysisProgressPhase.Explaining,
        "finishing" => AnalysisProgressPhase.Finishing,
        _ => null,
    };
}
