namespace DiffHacker.Core.Providers;

/// <summary>
/// The provider families DiffHacker can talk to.
/// <para>
/// This is the domain spelling. The generated contracts carry their own enums, one per
/// wire type, because JSON Schema cannot share a definition across files without
/// duplicating the generated type. <c>ProviderTypeWire</c> in the host maps between them,
/// and a test asserts all three sets agree.
/// </para>
/// </summary>
public enum LlmProviderType
{
    OpenAi,
    Anthropic,
    Gemini,
    Grok,
    DeepSeek,

    /// <summary>Any endpoint speaking the OpenAI wire format, including local runtimes.</summary>
    OpenAiCompatible,
}
