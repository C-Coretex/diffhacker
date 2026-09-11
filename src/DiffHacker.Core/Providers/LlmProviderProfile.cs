namespace DiffHacker.Core.Providers;

/// <summary>
/// A configured LLM provider as it is persisted.
/// <para>
/// Deliberately carries no API key. Keys live only in <c>ISecretStore</c>, under the name
/// <see cref="SecretName"/> derives, so no accidental serialisation of this record can leak
/// one (CLAUDE.md §0.2.13).
/// </para>
/// </summary>
public sealed record LlmProviderProfile
{
    public required string Id { get; init; }

    public required LlmProviderType ProviderType { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Model identifier as free text. Never validated against a hardcoded list.</summary>
    public required string Model { get; init; }

    /// <summary>Endpoint override. Required for <see cref="LlmProviderType.OpenAiCompatible"/>.</summary>
    public string? BaseUrl { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>
    /// Models the last successful connection test reported. These are the suggestions the
    /// model field offers; there is no hardcoded list anywhere, because those rot.
    /// </summary>
    public IReadOnlyList<string> ModelSuggestions { get; init; } = [];

    /// <summary>
    /// US dollars per million input tokens, overriding the bundled price table.
    /// <para>
    /// The table ships with the application and therefore goes stale between releases, exactly
    /// the way a hardcoded model list would. This is the escape hatch: a user on a model the
    /// table has never heard of, or one whose price moved, types the current rate and gets a
    /// real cost estimate instead of "unknown".
    /// </para>
    /// </summary>
    public decimal? InputCostPerMillion { get; init; }

    /// <summary>US dollars per million output tokens. See <see cref="InputCostPerMillion"/>.</summary>
    public decimal? OutputCostPerMillion { get; init; }

    /// <summary>
    /// The override as a rate, or null unless <b>both</b> halves are set — a price with only
    /// one side would silently bill output at zero.
    /// </summary>
    public Llm.LlmModelRate? CostOverride =>
        InputCostPerMillion is { } input && OutputCostPerMillion is { } output
            ? new Llm.LlmModelRate { InputPerMillion = input, OutputPerMillion = output }
            : null;

    /// <summary>
    /// The model's context window in tokens, overriding the bundled model table.
    /// <para>
    /// The same escape hatch <see cref="InputCostPerMillion"/> is, for the same reason: the table
    /// ships with the application and goes stale, and a user on a model it has never heard of
    /// should be able to say how big the window is rather than be told it is unknown forever.
    /// Unlike the cost pair this stands alone — half a rate bills the other half at zero, but half
    /// a context window is not a thing.
    /// </para>
    /// <para>
    /// <b>This is not a budget.</b> It is displayed during a run and consulted nowhere else.
    /// <c>LlmBudget</c> does not know about it and <c>LlmSession</c> never stops a run for
    /// exceeding it — wiring it into the budget is exactly the helpful-looking change a later
    /// reader would make, and it would turn a meter into a kill switch the user never asked for.
    /// </para>
    /// </summary>
    public int? ContextWindowTokens { get; init; }

    /// <summary>
    /// The override, or null when none is set and the bundled table should be consulted instead.
    /// Named to read the same way <see cref="CostOverride"/> does at the call site.
    /// </summary>
    public int? ContextWindowOverride => ContextWindowTokens is > 0 ? ContextWindowTokens : null;

    /// <summary>
    /// Overrides <see cref="Llm.LlmBudget.Default"/>'s tool-call ceiling for runs on this profile.
    /// <para>
    /// The same escape hatch <see cref="InputCostPerMillion"/> is: a runaway guard sized for one
    /// profile is not sized for all of them, and a user who knows their model needs more room
    /// before a run is genuinely stuck — or wants a tighter one — sets it here instead of living
    /// with one number across every provider. Null or non-positive keeps the default.
    /// </para>
    /// </summary>
    public int? MaxToolCallsOverride { get; init; }

    /// <summary>Overrides <see cref="Llm.LlmBudget.Default"/>'s token ceiling. See <see cref="MaxToolCallsOverride"/>.</summary>
    public long? MaxTotalTokensOverride { get; init; }

    /// <summary>
    /// The budget a run on this profile starts with: <see cref="Llm.LlmBudget.Default"/> with
    /// whichever of <see cref="MaxToolCallsOverride"/> and <see cref="MaxTotalTokensOverride"/> are
    /// actually set.
    /// </summary>
    public Llm.LlmBudget EffectiveBudget => Llm.LlmBudget.Default with
    {
        MaxToolCalls = MaxToolCallsOverride is > 0 ? MaxToolCallsOverride.Value : Llm.LlmBudget.Default.MaxToolCalls,
        MaxTotalTokens = MaxTotalTokensOverride is > 0
            ? MaxTotalTokensOverride.Value
            : Llm.LlmBudget.Default.MaxTotalTokens,
    };

    /// <summary>Name this profile's API key is stored under in the secret store.</summary>
    public static string SecretName(string profileId) => "provider:" + profileId;
}
