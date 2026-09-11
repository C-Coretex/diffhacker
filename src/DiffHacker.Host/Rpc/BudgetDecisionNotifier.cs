using System.Collections.Concurrent;
using DiffHacker.Contracts;
using DiffHacker.Core.Llm;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Asks the renderer whether a run that hit a budget limit should keep going, and waits for the
/// answer.
/// <para>
/// No new request/response RPC direction is needed for this: the host already notifies the
/// renderer freely (<see cref="IRpcNotifier"/>), and the renderer already calls the host freely.
/// This sends <c>run.budgetLimitReached</c> and then <i>waits</i> — not on a reply to that
/// notification, which JSON-RPC notifications do not have, but on the renderer's own follow-up
/// call to <c>run.answerBudgetPrompt</c>, matched by <see cref="BudgetLimitReached.PromptId"/>.
/// <see cref="BudgetPromptRpcTarget"/> is where that call lands and resolves the wait.
/// </para>
/// <para>
/// One outstanding prompt per <see cref="AskAsync"/> call, keyed by a fresh id each time — a run
/// can only be paused at one limit at once, but the dictionary costs nothing and removes the
/// assumption.
/// </para>
/// </summary>
public sealed partial class BudgetDecisionNotifier(IRpcNotifier notifier, ILogger<BudgetDecisionNotifier> logger)
    : IBudgetDecisionPrompt, IBudgetPromptResolver
{
    /// <summary>The notification method name. Mirrored in the renderer's RpcNotifications.</summary>
    public const string Method = "run.budgetLimitReached";

    private readonly ConcurrentDictionary<string, TaskCompletionSource<BudgetDecision>> _pending =
        new(StringComparer.Ordinal);

    public async Task<BudgetDecision> AskAsync(LlmBudgetLimitReached limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limit);

        var promptId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<BudgetDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[promptId] = tcs;

        try
        {
            await notifier.NotifyAsync(Method, ToWire(promptId, limit), cancellationToken).ConfigureAwait(false);
            PromptSent(logger, promptId, limit.Limit);

            await using (cancellationToken.Register(
                static state => ((TaskCompletionSource<BudgetDecision>)state!).TrySetCanceled(),
                tcs).ConfigureAwait(false))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            _pending.TryRemove(promptId, out _);
        }
    }

    /// <summary>
    /// Delivers the renderer's answer to the matching wait. False when nothing is waiting under
    /// that id any more — already answered, or the run ended some other way — which
    /// <see cref="BudgetPromptRpcTarget"/> treats as a no-op rather than an error, since a
    /// renderer racing a cancel against a click is expected.
    /// </summary>
    public bool TryResolve(string promptId, BudgetDecision decision)
    {
        if (_pending.TryGetValue(promptId, out var tcs) && tcs.TrySetResult(decision))
        {
            PromptAnswered(logger, promptId, decision);
            return true;
        }

        return false;
    }

    private static BudgetLimitReached ToWire(string promptId, LlmBudgetLimitReached limit) => new(
        costUsd: limit.CostUsd is { } cost ? (double)cost : null,
        explanation: limit.Explanation,
        limit: limit.Limit switch
        {
            LlmBudgetLimit.ToolCalls => BudgetLimitKind.Tool_calls,
            LlmBudgetLimit.Tokens => BudgetLimitKind.Tokens,
            LlmBudgetLimit.Turns => BudgetLimitKind.Turns,
            LlmBudgetLimit.Cost => BudgetLimitKind.Cost,
            _ => throw new ArgumentOutOfRangeException(nameof(limit)),
        },
        promptId: promptId,
        tokensUsed: (int)Math.Min(limit.TokensUsed, int.MaxValue),
        toolCallsUsed: limit.ToolCallsUsed,
        turnsUsed: limit.TurnsUsed);

    [LoggerMessage(
        EventId = 7201,
        Level = LogLevel.Information,
        Message = "Run paused at prompt {PromptId}, asking whether to continue past its {Limit} limit.")]
    private static partial void PromptSent(ILogger logger, string promptId, LlmBudgetLimit limit);

    [LoggerMessage(
        EventId = 7202,
        Level = LogLevel.Information,
        Message = "Prompt {PromptId} answered: {Decision}.")]
    private static partial void PromptAnswered(ILogger logger, string promptId, BudgetDecision decision);
}

/// <summary>Delivers a renderer's answer to a budget prompt to whoever is waiting on it.</summary>
public interface IBudgetPromptResolver
{
    /// <returns>False when <paramref name="promptId"/> is not currently waiting.</returns>
    bool TryResolve(string promptId, BudgetDecision decision);
}
