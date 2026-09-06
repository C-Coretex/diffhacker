using DiffHacker.Core.Providers;

namespace DiffHacker.Llm.Live.Tests;

/// <summary>
/// One provider this suite can be pointed at, and the environment variable that switches it on.
/// <para>
/// The whole suite is opt-in. Nothing here runs unless the variable is set, so
/// <c>dotnet test src/DiffHacker.slnx</c> stays offline and green for everyone — which
/// requirement 9 and CLAUDE.md both insist on — while still leaving a way to prove the thing
/// the "Done when" bar actually asks for.
/// </para>
/// </summary>
internal sealed record LiveProvider
{
    public required LlmProviderType Type { get; init; }

    /// <summary>The environment variable holding the API key.</summary>
    public required string KeyVariable { get; init; }

    /// <summary>
    /// Environment variable naming the model to use, so a run is not pinned to a model
    /// identifier that will be retired. Falls back to <see cref="DefaultModel"/>.
    /// </summary>
    public required string ModelVariable { get; init; }

    public required string DefaultModel { get; init; }

    /// <summary>Environment variable holding a base URL, for the compatible endpoint only.</summary>
    public string? BaseUrlVariable { get; init; }

    public string? ApiKey => Read(KeyVariable);

    /// <summary>
    /// The key actually handed to the client. A local runtime such as Ollama authenticates
    /// nobody but the OpenAI client still requires a credential, so the compatible endpoint
    /// gets a placeholder when none was supplied.
    /// </summary>
    public string EffectiveApiKey => ApiKey ?? "not-required";

    public string Model => Read(ModelVariable) ?? DefaultModel;

    public string? BaseUrl => BaseUrlVariable is null ? null : Read(BaseUrlVariable);

    /// <summary>Why this provider is being skipped, or null when it can run.</summary>
    public string? SkipReason
    {
        get
        {
            if (BaseUrlVariable is not null)
            {
                // The compatible endpoint is identified by its URL, not its key: a local
                // runtime has no key to give.
                return string.IsNullOrWhiteSpace(BaseUrl)
                    ? $"Set {BaseUrlVariable} to run the OpenAI-compatible conformance check "
                      + "(a local Ollama at http://127.0.0.1:11434/v1 is the cheapest proof)."
                    : null;
            }

            return string.IsNullOrWhiteSpace(ApiKey)
                ? $"Set {KeyVariable} to run this provider's conformance check."
                : null;
        }
    }

    public LlmProviderProfile ToProfile() => new()
    {
        Id = "live-" + ProviderTypeNames.ToStorage(Type),
        ProviderType = Type,
        DisplayName = ProviderTypeNames.ToStorage(Type),
        Model = Model,
        BaseUrl = BaseUrl,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static string? Read(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// The five providers Iteration 4 names, plus one generic OpenAI-compatible endpoint.
    /// </summary>
    public static IReadOnlyList<LiveProvider> All { get; } =
    [
        new()
        {
            Type = LlmProviderType.OpenAi,
            KeyVariable = "DIFFHACKER_LIVE_OPENAI_KEY",
            ModelVariable = "DIFFHACKER_LIVE_OPENAI_MODEL",
            DefaultModel = "gpt-4o-mini",
        },
        new()
        {
            Type = LlmProviderType.Anthropic,
            KeyVariable = "DIFFHACKER_LIVE_ANTHROPIC_KEY",
            ModelVariable = "DIFFHACKER_LIVE_ANTHROPIC_MODEL",
            DefaultModel = "claude-haiku-4-5",
        },
        new()
        {
            Type = LlmProviderType.Gemini,
            KeyVariable = "DIFFHACKER_LIVE_GEMINI_KEY",
            ModelVariable = "DIFFHACKER_LIVE_GEMINI_MODEL",
            DefaultModel = "gemini-2.5-flash",
        },
        new()
        {
            Type = LlmProviderType.Grok,
            KeyVariable = "DIFFHACKER_LIVE_GROK_KEY",
            ModelVariable = "DIFFHACKER_LIVE_GROK_MODEL",
            DefaultModel = "grok-3-mini",
        },
        new()
        {
            Type = LlmProviderType.DeepSeek,
            KeyVariable = "DIFFHACKER_LIVE_DEEPSEEK_KEY",
            ModelVariable = "DIFFHACKER_LIVE_DEEPSEEK_MODEL",
            DefaultModel = "deepseek-chat",
        },
        new()
        {
            // Ollama needs no real key, but the client requires one, so any string does.
            Type = LlmProviderType.OpenAiCompatible,
            KeyVariable = "DIFFHACKER_LIVE_COMPATIBLE_KEY",
            ModelVariable = "DIFFHACKER_LIVE_COMPATIBLE_MODEL",
            DefaultModel = "llama3.1",
            BaseUrlVariable = "DIFFHACKER_LIVE_COMPATIBLE_BASEURL",
        },
    ];

    public static LiveProvider For(LlmProviderType type) => All.Single(provider => provider.Type == type);
}
