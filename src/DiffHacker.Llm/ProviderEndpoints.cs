using DiffHacker.Core.Providers;

namespace DiffHacker.Llm;

/// <summary>
/// Where each provider lives, and how it wants to be authenticated.
/// <para>
/// Iteration 2 needed only the model-listing endpoint. Iteration 4 added
/// <see cref="ChatBaseUrl"/> for conversations, which for every provider but one is the same
/// address — see the note there for why Gemini differs.
/// </para>
/// <para>
/// Note there are no model names here. Requirement 4 is explicit that hardcoded model lists
/// rot, so suggestions come from whatever the provider reports for the user's own key.
/// </para>
/// </summary>
internal static class ProviderEndpoints
{
    /// <summary>Default base URL for the model listing, or null when the user must supply one.</summary>
    public static string? DefaultBaseUrl(LlmProviderType type) => type switch
    {
        LlmProviderType.OpenAi => "https://api.openai.com/v1",
        LlmProviderType.Anthropic => "https://api.anthropic.com/v1",
        LlmProviderType.Gemini => "https://generativelanguage.googleapis.com/v1beta",
        LlmProviderType.Grok => "https://api.x.ai/v1",
        LlmProviderType.DeepSeek => "https://api.deepseek.com",

        // An OpenAI-compatible endpoint is by definition somewhere we cannot guess. The user
        // supplies it, and the form requires it.
        LlmProviderType.OpenAiCompatible => null,

        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown provider type."),
    };

    /// <summary>
    /// The model-listing endpoint, relative to the base URL. Every provider spells it the same
    /// way; the differences are all in the base URL and the authentication header.
    /// </summary>
    public const string ModelsPath = "models";

    /// <summary>
    /// Base URL for chat completions. A base URL configured on the profile always wins; this
    /// supplies the default.
    /// <para>
    /// Gemini is the only provider where this differs from <see cref="DefaultBaseUrl"/>. Its
    /// native API is not OpenAI-shaped, but Google publishes an OpenAI-compatible surface at
    /// <c>/v1beta/openai/</c> which supports tool calling, <c>json_schema</c> response formats
    /// and usage reporting. Using it means Gemini shares the OpenAI SDK path with Grok,
    /// DeepSeek and every user-supplied endpoint, and no third-party Gemini package enters the
    /// dependency graph. Google labels that surface beta, and a handful of Gemini-only
    /// controls are not reachable through it; neither is needed here.
    /// </para>
    /// <para>
    /// The model listing stays on the native base URL, because the compatible surface does not
    /// answer <c>GET /models</c> in the shape <c>ModelListParser</c> expects.
    /// </para>
    /// </summary>
    /// <para>
    /// Anthropic differs the other way: its SDK appends its own <c>/v1/messages</c>, so the
    /// base URL it wants is the host on its own, without the <c>/v1</c> the listing endpoint
    /// needs.
    /// </para>
    public static string? ChatBaseUrl(LlmProviderType type) => type switch
    {
        LlmProviderType.Gemini => GeminiCompatibleSuffix(DefaultBaseUrl(type)!),
        LlmProviderType.Anthropic => WithoutApiVersion(DefaultBaseUrl(type)!),
        _ => DefaultBaseUrl(type),
    };

    /// <summary>
    /// Points a Gemini base URL at Google's OpenAI-compatible surface, leaving one that
    /// already does alone. Applied to a user-supplied override too, because someone who typed
    /// the native endpoint into the settings form meant "talk to Gemini here", not "talk to
    /// Gemini in a dialect it does not speak".
    /// </summary>
    public static string GeminiCompatibleSuffix(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        return baseUrl.Contains("/openai", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl.TrimEnd('/') + "/openai/";
    }

    /// <summary>
    /// Strips a trailing <c>/v1</c>, which the Anthropic SDK adds for itself.
    /// </summary>
    public static string WithoutApiVersion(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var trimmed = baseUrl.TrimEnd('/');
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^3]
            : trimmed;
    }

    /// <summary>
    /// Applies the provider's authentication scheme. The three shapes in use are a bearer
    /// token, Anthropic's <c>x-api-key</c>, and Google's <c>x-goog-api-key</c>.
    /// </summary>
    public static void Authenticate(HttpRequestMessage request, LlmProviderType type, string apiKey)
    {
        switch (type)
        {
            case LlmProviderType.Anthropic:
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
                break;

            case LlmProviderType.Gemini:
                request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
                break;

            default:
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                break;
        }
    }

    /// <summary>
    /// Anthropic requires an explicit API version header on every request. Pinned rather than
    /// tracked: a newer version changes response shapes, and this code only reads model ids.
    /// </summary>
    public const string AnthropicVersion = "2023-06-01";
}
