using DiffHacker.Contracts;
using DiffHacker.Core.Providers;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Maps between the domain's <see cref="LlmProviderType"/> and the generated wire enums.
/// <para>
/// There are two generated enums for one concept because JSON Schema cannot share a definition
/// across files without the code generator duplicating the type into both outputs. Rather than
/// let a wire spelling become the domain spelling, the boundary translates — and
/// <c>ProviderTypeAgreementTests</c> asserts all three sets carry identical values, so a new
/// provider cannot be added to one and forgotten in the others.
/// </para>
/// </summary>
internal static class ProviderTypeWire
{
    public static LlmProviderType ToDomain(SaveProviderRequestProviderType wire) => wire switch
    {
        SaveProviderRequestProviderType.Openai => LlmProviderType.OpenAi,
        SaveProviderRequestProviderType.Anthropic => LlmProviderType.Anthropic,
        SaveProviderRequestProviderType.Gemini => LlmProviderType.Gemini,
        SaveProviderRequestProviderType.Grok => LlmProviderType.Grok,
        SaveProviderRequestProviderType.Deepseek => LlmProviderType.DeepSeek,
        SaveProviderRequestProviderType.Openai_compatible => LlmProviderType.OpenAiCompatible,
        _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, "Unknown provider type."),
    };

    public static ProviderProfileProviderType ToWire(LlmProviderType domain) => domain switch
    {
        LlmProviderType.OpenAi => ProviderProfileProviderType.Openai,
        LlmProviderType.Anthropic => ProviderProfileProviderType.Anthropic,
        LlmProviderType.Gemini => ProviderProfileProviderType.Gemini,
        LlmProviderType.Grok => ProviderProfileProviderType.Grok,
        LlmProviderType.DeepSeek => ProviderProfileProviderType.Deepseek,
        LlmProviderType.OpenAiCompatible => ProviderProfileProviderType.Openai_compatible,
        _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown provider type."),
    };
}
