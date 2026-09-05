using System.Runtime.Serialization;
using DiffHacker.Contracts;

namespace DiffHacker.Contracts.Tests;

/// <summary>
/// One concept, three enums.
/// <para>
/// JSON Schema cannot share a definition across files without the generator duplicating the
/// type into every output, so <c>ProviderProfile</c> and <c>SaveProviderRequest</c> each carry
/// their own generated enum, and the domain carries a third. That is a survivable amount of
/// duplication only while something checks the three cannot drift — which is this.
/// </para>
/// </summary>
public sealed class ProviderTypeAgreementTests
{
    private static readonly string[] Expected =
        ["openai", "anthropic", "gemini", "grok", "deepseek", "openai_compatible"];

    [Fact]
    public void The_outbound_enum_carries_exactly_the_declared_provider_types()
    {
        WireValues<ProviderProfileProviderType>().ShouldBe(Expected, ignoreOrder: true);
    }

    [Fact]
    public void The_inbound_enum_carries_exactly_the_declared_provider_types()
    {
        WireValues<SaveProviderRequestProviderType>().ShouldBe(Expected, ignoreOrder: true);
    }

    [Fact]
    public void The_two_generated_enums_agree_with_each_other()
    {
        // Adding a provider to one schema and forgetting the other would otherwise be a
        // run-time surprise at the RPC boundary rather than a build failure.
        WireValues<SaveProviderRequestProviderType>()
            .ShouldBe(WireValues<ProviderProfileProviderType>(), ignoreOrder: true);
    }

    private static string[] WireValues<TEnum>()
        where TEnum : struct, Enum =>
        [.. typeof(TEnum)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(field => field.GetCustomAttributes(typeof(EnumMemberAttribute), false)
                .Cast<EnumMemberAttribute>()
                .FirstOrDefault()?.Value ?? field.Name)];
}
