namespace DiffHacker.Core.Llm;

/// <summary>
/// Tokens consumed, and what they cost if that is knowable.
/// <para>
/// Providers disagree about what they report. Some give cached-input tokens separately, some
/// fold them into the input count, some report nothing at all on a streamed response. The
/// invariant here is that a zero is a zero and an unknown is an unknown: a provider that
/// reported no usage leaves <see cref="IsReported"/> false rather than claiming a free request.
/// </para>
/// </summary>
public readonly record struct LlmUsage
{
    /// <summary>Nothing consumed, nothing reported. The identity for <see cref="op_Addition"/>.</summary>
    public static LlmUsage None => default;

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    /// <summary>
    /// Input tokens served from the provider's prompt cache, where it distinguishes them.
    /// Always a subset of <see cref="InputTokens"/>, never additional to it.
    /// </summary>
    public long CachedInputTokens { get; init; }

    /// <summary>
    /// Whether the provider actually told us. False means "we do not know", which is a
    /// different claim from "it was free" and is presented differently.
    /// </summary>
    public bool IsReported { get; init; }

    /// <summary>
    /// Estimated cost in US dollars, or null when no rate is known for the model. Never
    /// defaulted to zero — see <c>ITokenPricing</c>.
    /// </summary>
    public decimal? EstimatedCostUsd { get; init; }

    public long TotalTokens => InputTokens + OutputTokens;

    public bool CostIsKnown => EstimatedCostUsd.HasValue;

    /// <summary>
    /// Accumulates one request into a running total.
    /// <para>
    /// Cost adds when both sides have one, and carries the side that does otherwise — which is
    /// what makes <see cref="None"/> a usable starting point. Every request in a run uses the
    /// same model, so in practice either all of them are priced or none are; there is no
    /// half-priced total to misrepresent.
    /// </para>
    /// </summary>
    public static LlmUsage operator +(LlmUsage left, LlmUsage right) => new()
    {
        InputTokens = left.InputTokens + right.InputTokens,
        OutputTokens = left.OutputTokens + right.OutputTokens,
        CachedInputTokens = left.CachedInputTokens + right.CachedInputTokens,
        IsReported = left.IsReported || right.IsReported,
        EstimatedCostUsd = left.EstimatedCostUsd is { } a && right.EstimatedCostUsd is { } b
            ? a + b
            : left.EstimatedCostUsd ?? right.EstimatedCostUsd,
    };

    /// <summary>Named alternative to <see cref="op_Addition"/>, for callers that prefer it.</summary>
    public static LlmUsage Add(LlmUsage left, LlmUsage right) => left + right;
}
