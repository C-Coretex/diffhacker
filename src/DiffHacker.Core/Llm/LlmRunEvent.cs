namespace DiffHacker.Core.Llm;

/// <summary>What just happened in a run.</summary>
public enum LlmRunEventKind
{
    TurnStarted,
    TurnFinished,
    ToolCallStarted,
    ToolCallFinished,
    UsageUpdated,
    RetryScheduled,
}

/// <summary>
/// A single observable moment in a run.
/// <para>
/// Deliberately not a token stream. The product's answer is one large JSON document produced
/// at the end, and §0.2.8 forbids revealing a half-built graph anyway — so what a live run view
/// can usefully show is progress through turns and tools, not characters arriving. Iteration 5
/// forwards these over the JSON-RPC notification channel alongside <c>report_progress</c>, and
/// Iteration 13 renders them.
/// </para>
/// <para>
/// One flat record with a discriminator rather than a hierarchy, because these cross the wire
/// and the contract generator cannot nest a <c>$def</c> inside another <c>$def</c> — see the
/// note in <c>schema/changeset-result.schema.json</c>.
/// </para>
/// </summary>
public sealed record LlmRunEvent
{
    public required LlmRunEventKind Kind { get; init; }

    /// <summary>Which turn this belongs to, from 1.</summary>
    public required int Turn { get; init; }

    /// <summary>Set on the tool-call events.</summary>
    public string? ToolName { get; init; }

    /// <summary>Set on <see cref="LlmRunEventKind.ToolCallStarted"/>: truncated arguments.</summary>
    public string? ArgumentsPreview { get; init; }

    /// <summary>Set on <see cref="LlmRunEventKind.ToolCallFinished"/>.</summary>
    public int? ResultBytes { get; init; }

    /// <summary>Set on <see cref="LlmRunEventKind.ToolCallFinished"/>.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Set on the finishing events when something went wrong.</summary>
    public bool IsError { get; init; }

    /// <summary>Running totals. Carried on every event so a view never has to accumulate.</summary>
    public LlmUsage CumulativeUsage { get; init; }

    /// <summary>Set on <see cref="LlmRunEventKind.RetryScheduled"/>: how long before the next try.</summary>
    public TimeSpan? RetryDelay { get; init; }

    /// <summary>
    /// Set on <see cref="LlmRunEventKind.RetryScheduled"/>: which attempt is about to be made,
    /// from 1.
    /// </summary>
    public int? RetryAttempt { get; init; }

    /// <summary>
    /// A <see cref="LlmFailures"/> code explaining a retry or a failed finish. Never prose —
    /// the renderer resolves it (§0.6).
    /// </summary>
    public string? ReasonCode { get; init; }
}
