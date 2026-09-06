namespace DiffHacker.Core.Llm;

/// <summary>How a run ended.</summary>
public enum LlmRunOutcome
{
    /// <summary>The model finished and, where a schema was required, produced valid JSON.</summary>
    Completed,

    /// <summary>
    /// The conversation outgrew the model's context window.
    /// <para>
    /// This is its own outcome rather than a failure code because Iteration 7 has to
    /// <i>react</i> to it — a changeset that overflows needs the run reshaped, not an error
    /// message. Requirement 6 asks for exactly this.
    /// </para>
    /// </summary>
    ContextOverflow,

    /// <summary>A hard stop in <see cref="LlmBudget"/> fired. Whatever exists is partial.</summary>
    BudgetExceeded,

    /// <summary>Anything else. <see cref="LlmRunResult.FailureCode"/> says which.</summary>
    Failed,
}

/// <summary>
/// The outcome of one run, complete with what it cost.
/// <para>
/// A failure is a result rather than an exception, matching
/// <see cref="Providers.ProviderConnectionResult"/>: the caller needs the translated code, the
/// provider's own wording <i>and</i> the usage side by side, and an exception carries at most
/// one of the three comfortably. Cancellation is the exception to that — it throws, because
/// that is what every caller of a <c>CancellationToken</c> already expects, and the usage
/// stays readable on the session afterwards.
/// </para>
/// </summary>
public sealed record LlmRunResult
{
    public required LlmRunOutcome Outcome { get; init; }

    /// <summary>
    /// The validated JSON answer when the conversation asked for a shape, otherwise null.
    /// </summary>
    public string? StructuredJson { get; init; }

    /// <summary>The model's final text. Present on a free-text run, and often on a failure.</summary>
    public string? Text { get; init; }

    /// <summary>A <see cref="LlmFailures"/> code. Null only when <see cref="LlmRunOutcome.Completed"/>.</summary>
    public string? FailureCode { get; init; }

    /// <summary>
    /// The provider's own error text, for the log and for the detail line under the translated
    /// headline. Scrubbed of the API key before it goes anywhere.
    /// </summary>
    public string? ProviderMessage { get; init; }

    /// <summary>Everything consumed, including the requests that failed.</summary>
    public LlmUsage Usage { get; init; }

    /// <summary>How many round trips to the provider this took.</summary>
    public int TurnCount { get; init; }

    /// <summary>Every tool call, in the order the model made them.</summary>
    public IReadOnlyList<LlmToolCallRecord> ToolCalls { get; init; } = [];

    public bool Succeeded => Outcome == LlmRunOutcome.Completed;
}
