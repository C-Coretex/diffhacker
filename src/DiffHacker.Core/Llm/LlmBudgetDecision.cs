namespace DiffHacker.Core.Llm;

/// <summary>Which <see cref="LlmBudget"/> field a run hit.</summary>
public enum LlmBudgetLimit
{
    ToolCalls,
    Tokens,
    Turns,
    Cost,
}

/// <summary>
/// One budget limit was reached mid-run: which, and how far the run had got.
/// <para>
/// Carried to <see cref="BudgetDecisionCallback"/> separately from the sentence
/// <see cref="LlmRunResult.ProviderMessage"/> would otherwise use, because a caller deciding
/// whether to let the run keep going needs the numbers on their own, not parsed back out of
/// prose meant for a person.
/// </para>
/// </summary>
public sealed record LlmBudgetLimitReached
{
    public required LlmBudgetLimit Limit { get; init; }

    /// <summary>The same sentence a hard stop would use, for a decision surface with no numbers of its own.</summary>
    public required string Explanation { get; init; }

    public required int ToolCallsUsed { get; init; }

    public required long TokensUsed { get; init; }

    public required int TurnsUsed { get; init; }

    public decimal? CostUsd { get; init; }
}

/// <summary>What the caller wants done about a reached limit.</summary>
public enum BudgetDecision
{
    /// <summary>Raise the limit that was hit by its original amount and keep going.</summary>
    Continue,

    /// <summary>Stop the run. Whatever exists is partial, exactly like a hard stop.</summary>
    Stop,
}

/// <summary>
/// Asked when a run hits one of <see cref="LlmBudget"/>'s limits, instead of the run failing
/// outright. A run given none of these keeps hard-stopping exactly as before — every existing
/// caller of <see cref="ILlmSession.RunAsync"/> that does not pass one is untouched.
/// </summary>
public delegate Task<BudgetDecision> BudgetDecisionCallback(
    LlmBudgetLimitReached limit,
    CancellationToken cancellationToken);

/// <summary>
/// Asks a human whether a run that hit a budget limit should keep going.
/// <para>
/// Implemented in the host as a notification to the renderer followed by a matching answer back
/// over the bridge (<c>run.budgetLimitReached</c> / <c>run.answerBudgetPrompt</c>) — see
/// <c>docs/decisions.md</c>. Both <c>AnalysisRunner</c> and <c>ProfileBuilder</c> take one of
/// these, so the same mechanism serves an analysis run and a repository profiling run alike.
/// </para>
/// </summary>
public interface IBudgetDecisionPrompt
{
    Task<BudgetDecision> AskAsync(LlmBudgetLimitReached limit, CancellationToken cancellationToken);
}
