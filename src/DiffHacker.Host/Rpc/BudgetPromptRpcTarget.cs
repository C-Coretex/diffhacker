using DiffHacker.Contracts;
using DiffHacker.Core.Llm;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Where the renderer's answer to a <c>run.budgetLimitReached</c> prompt lands.
/// <para>
/// Its own small target rather than a method on <see cref="AnalysisRpcTarget"/> or
/// <see cref="ProfileRpcTarget"/>: a budget prompt can come from either kind of run, and this
/// method does not need to know which — it only resolves a promise
/// <see cref="BudgetDecisionNotifier"/> is already holding, keyed by the id on the notification
/// the reviewer is responding to.
/// </para>
/// </summary>
public sealed class BudgetPromptRpcTarget(IBudgetPromptResolver resolver)
{
    [JsonRpcMethod("run.answerBudgetPrompt")]
    public Task AnswerAsync(AnswerBudgetPromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        resolver.TryResolve(request.PromptId, FromWire(request.Decision));
        return Task.CompletedTask;
    }

    private static BudgetDecision FromWire(BudgetPromptDecision decision) => decision switch
    {
        BudgetPromptDecision.Continue => BudgetDecision.Continue,
        BudgetPromptDecision.Stop => BudgetDecision.Stop,
        _ => BudgetDecision.Stop,
    };
}
