using DiffHacker.Core.Providers;

namespace DiffHacker.Core.Llm;

/// <summary>
/// What a model costs per token, when that is known.
/// <para>
/// Hardcoded prices rot exactly the way hardcoded model lists do (Iteration 2, requirement 4),
/// so the answer here is deliberately allowed to be "no idea". A run whose model has no rate
/// reports its tokens and says the cost is unknown; it never reports zero, and it never
/// guesses from a neighbouring model.
/// </para>
/// </summary>
public interface ITokenPricing
{
    /// <summary>
    /// The rate for <paramref name="model"/> on <paramref name="providerType"/>, or false when
    /// none is known.
    /// </summary>
    bool TryGetRate(LlmProviderType providerType, string model, out LlmModelRate rate);

    /// <summary>
    /// When the bundled table was compiled. Shown beside an estimate so the user knows how old
    /// the number behind it is.
    /// </summary>
    DateOnly TableAsOf { get; }
}

/// <summary>
/// US dollars per million tokens.
/// <para>
/// Per million rather than per token because that is the unit every provider publishes, and
/// converting at the point of display is how a price ends up transcribed wrongly.
/// </para>
/// </summary>
public readonly record struct LlmModelRate
{
    public required decimal InputPerMillion { get; init; }

    public required decimal OutputPerMillion { get; init; }

    /// <summary>
    /// Rate for input tokens served from the provider's prompt cache. Null when the provider
    /// does not price them separately, in which case cached tokens are billed as input.
    /// </summary>
    public decimal? CachedInputPerMillion { get; init; }

    /// <summary>
    /// Applies this rate to a usage record. Cached tokens are a subset of the input count, so
    /// they are subtracted before the full input rate is applied.
    /// </summary>
    public decimal CostOf(LlmUsage usage)
    {
        var cached = CachedInputPerMillion is null ? 0 : usage.CachedInputTokens;
        var fullPriceInput = usage.InputTokens - cached;

        return ((fullPriceInput * InputPerMillion)
                + (cached * (CachedInputPerMillion ?? InputPerMillion))
                + (usage.OutputTokens * OutputPerMillion))
               / 1_000_000m;
    }
}
