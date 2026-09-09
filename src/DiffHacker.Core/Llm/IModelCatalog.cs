using DiffHacker.Core.Providers;

namespace DiffHacker.Core.Llm;

/// <summary>
/// What is known about a model: what it costs per token, and how large its context window is.
/// <para>
/// Hardcoded facts about models rot exactly the way hardcoded model lists do (Iteration 2,
/// requirement 4), so every answer here is deliberately allowed to be "no idea". A run whose model
/// has no rate reports its tokens and says the cost is unknown; it never reports zero, and it never
/// guesses from a neighbouring model. A model with no known context window gets a meter that shows
/// an absolute token count and no percentage, for the same reason.
/// </para>
/// <para>
/// Named for what it is rather than for what it was. It began as a pricing interface, and
/// Iteration 8 gave it the context window because that is the same kind of fact from the same
/// bundled snapshot — but an interface called <c>ITokenPricing</c> that returns a context window
/// is a name that lies to the next reader.
/// </para>
/// </summary>
public interface IModelCatalog
{
    /// <summary>
    /// The rate for <paramref name="model"/> on <paramref name="providerType"/>, or false when
    /// none is known.
    /// </summary>
    bool TryGetRate(LlmProviderType providerType, string model, out LlmModelRate rate);

    /// <summary>
    /// The context window of <paramref name="model"/> in tokens, or false when the table has never
    /// heard of it. False rather than a plausible default: the number is shown as a denominator,
    /// and a wrong denominator makes a meter that is confidently wrong instead of honestly blank.
    /// <para>
    /// Informational. Nothing in <c>LlmBudget</c> consults this, and no run is ever stopped for
    /// exceeding it.
    /// </para>
    /// </summary>
    bool TryGetContextWindow(LlmProviderType providerType, string model, out int tokens);

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
