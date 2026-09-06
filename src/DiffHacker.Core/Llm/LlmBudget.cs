namespace DiffHacker.Core.Llm;

/// <summary>
/// The hard stops on one run.
/// <para>
/// These exist because a tool-using loop can fail in a way a single request cannot: the model
/// keeps calling tools, learns nothing, and spends real money doing it. Every limit here is a
/// runaway guard, not a cost control — Iteration 13's pre-run estimate is where an expensive
/// run gets prevented, because killing one mid-flight wastes everything already spent.
/// </para>
/// <para>
/// Iteration 13 makes these user-configurable. Until then <see cref="Default"/> is what every
/// run uses, and hitting any of them produces <see cref="LlmRunOutcome.BudgetExceeded"/> with
/// an explanation of what was and was not produced (§0.2.5, §0.2.8).
/// </para>
/// </summary>
public sealed record LlmBudget
{
    /// <summary>
    /// Sized so a 1000-file changeset can genuinely finish. A run that needs more than 500 tool
    /// calls or 300 turns is not exploring, it is stuck.
    /// </summary>
    public static LlmBudget Default { get; } = new();

    public int MaxToolCalls { get; init; } = 500;

    public int MaxTurns { get; init; } = 300;

    /// <summary>Input plus output, across the whole run.</summary>
    public long MaxTotalTokens { get; init; } = 2_000_000;

    /// <summary>
    /// Off by default. A mid-run cost kill throws away the tokens already paid for and
    /// produces nothing, which is worse for the user than an expensive run they were warned
    /// about beforehand.
    /// </summary>
    public decimal? MaxCostUsd { get; init; }

    /// <summary>
    /// One request, including its retries. Long enough for a reasoning model on a large prompt.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How many times a transient failure is retried before giving up. Non-transient failures
    /// are never retried regardless.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 5;

    /// <summary>
    /// Consecutive failing tool calls before the run is abandoned. Three in a row means the
    /// model is not reading the error it is being handed.
    /// </summary>
    public int MaxConsecutiveToolFailures { get; init; } = 3;
}
