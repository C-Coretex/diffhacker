using DiffHacker.Core.Providers;

namespace DiffHacker.Llm;

/// <summary>
/// Where each provider's model listing lives, and how it wants to be authenticated.
/// <para>
/// This is the whole of the per-provider knowledge Iteration 2 needs. Iteration 4 replaces it
/// with <c>Microsoft.Extensions.AI</c> clients; nothing here tries to anticipate that.
/// </para>
/// <para>
/// Note there are no model names here. Requirement 4 is explicit that hardcoded model lists
/// rot, so suggestions come from whatever the provider reports for the user's own key.
/// </para>
/// </summary>
internal static class ProviderEndpoints
{
    /// <summary>Default base URL, or null when the user must supply one.</summary>
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
