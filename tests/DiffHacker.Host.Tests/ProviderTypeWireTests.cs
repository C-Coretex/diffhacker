using DiffHacker.Contracts;
using DiffHacker.Core.Providers;
using DiffHacker.Host.Rpc;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The third side of the provider-type triangle: the domain enum against the two generated
/// ones. <c>ProviderTypeAgreementTests</c> in the contracts suite checks the generated pair
/// agree with each other; this checks the boundary actually maps every value.
/// </summary>
public sealed class ProviderTypeWireTests
{
    [Theory]
    [InlineData(LlmProviderType.OpenAi)]
    [InlineData(LlmProviderType.Anthropic)]
    [InlineData(LlmProviderType.Gemini)]
    [InlineData(LlmProviderType.Grok)]
    [InlineData(LlmProviderType.DeepSeek)]
    [InlineData(LlmProviderType.OpenAiCompatible)]
    public void Every_domain_value_maps_outbound(LlmProviderType domain)
    {
        // Throws rather than returning a default if a case is missing, so this is a real check.
        _ = ProviderTypeWire.ToWire(domain);
    }

    [Theory]
    [InlineData(SaveProviderRequestProviderType.Openai, LlmProviderType.OpenAi)]
    [InlineData(SaveProviderRequestProviderType.Anthropic, LlmProviderType.Anthropic)]
    [InlineData(SaveProviderRequestProviderType.Gemini, LlmProviderType.Gemini)]
    [InlineData(SaveProviderRequestProviderType.Grok, LlmProviderType.Grok)]
    [InlineData(SaveProviderRequestProviderType.Deepseek, LlmProviderType.DeepSeek)]
    [InlineData(SaveProviderRequestProviderType.Openai_compatible, LlmProviderType.OpenAiCompatible)]
    public void Every_inbound_value_maps_to_the_matching_domain_value(
        SaveProviderRequestProviderType wire,
        LlmProviderType expected)
    {
        ProviderTypeWire.ToDomain(wire).ShouldBe(expected);
    }

    [Fact]
    public void The_domain_enum_has_no_value_the_wire_cannot_express()
    {
        Enum.GetValues<LlmProviderType>().Length.ShouldBe(
            Enum.GetValues<SaveProviderRequestProviderType>().Length,
            "A provider added to the domain but not to the schemas would fail at the boundary.");
    }

    [Theory]
    [InlineData(LlmProviderType.OpenAi, "openai")]
    [InlineData(LlmProviderType.OpenAiCompatible, "openai_compatible")]
    [InlineData(LlmProviderType.DeepSeek, "deepseek")]
    public void The_persisted_spelling_matches_the_wire_spelling(LlmProviderType domain, string expected)
    {
        // Keeping these identical means a stored profile and a wire payload can be compared by
        // eye when something goes wrong in the field.
        ProviderTypeNames.ToStorage(domain).ShouldBe(expected);
        ProviderTypeNames.FromStorage(expected).ShouldBe(domain);
    }
}
