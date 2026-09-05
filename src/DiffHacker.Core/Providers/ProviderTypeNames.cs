namespace DiffHacker.Core.Providers;

/// <summary>
/// The stable string spelling of <see cref="LlmProviderType"/>.
/// <para>
/// Deliberately identical to the values declared in the JSON Schemas, so the persisted form,
/// the wire form and the domain form never drift apart. A test asserts that.
/// </para>
/// </summary>
public static class ProviderTypeNames
{
    public const string OpenAi = "openai";
    public const string Anthropic = "anthropic";
    public const string Gemini = "gemini";
    public const string Grok = "grok";
    public const string DeepSeek = "deepseek";
    public const string OpenAiCompatible = "openai_compatible";

    public static string ToStorage(LlmProviderType type) => type switch
    {
        LlmProviderType.OpenAi => OpenAi,
        LlmProviderType.Anthropic => Anthropic,
        LlmProviderType.Gemini => Gemini,
        LlmProviderType.Grok => Grok,
        LlmProviderType.DeepSeek => DeepSeek,
        LlmProviderType.OpenAiCompatible => OpenAiCompatible,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown provider type."),
    };

    public static LlmProviderType FromStorage(string value) => value switch
    {
        OpenAi => LlmProviderType.OpenAi,
        Anthropic => LlmProviderType.Anthropic,
        Gemini => LlmProviderType.Gemini,
        Grok => LlmProviderType.Grok,
        DeepSeek => LlmProviderType.DeepSeek,
        OpenAiCompatible => LlmProviderType.OpenAiCompatible,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown provider type."),
    };
}
