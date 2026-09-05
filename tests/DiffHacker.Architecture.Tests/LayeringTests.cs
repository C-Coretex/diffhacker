using System.Reflection;

namespace DiffHacker.Architecture.Tests;

/// <summary>
/// The structural rules from CLAUDE.md §0.3, enforced rather than documented.
/// <para>
/// These are cheap to keep true and expensive to restore once broken, which is exactly the
/// kind of rule that belongs in a test rather than in a review checklist.
/// </para>
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Core = typeof(DiffHacker.Core.Providers.LlmProviderType).Assembly;

    // Anchored on real types now that these projects have content; DiffHacker.Tools is still
    // empty and keeps its placeholder until Iteration 5 fills it.
    public static TheoryData<Assembly> DomainAssemblies() =>
    [
        typeof(DiffHacker.Core.Providers.LlmProviderType).Assembly,
        typeof(DiffHacker.Git.GitProcessRunner).Assembly,
        typeof(DiffHacker.Llm.HttpProviderConnectionTester).Assembly,
        typeof(DiffHacker.Storage.AppDatabase).Assembly,
        typeof(DiffHacker.Tools.AssemblyMarker).Assembly,
    ];

    [Fact]
    public void Core_does_not_reference_the_host()
    {
        ReferencedAssemblyNames(Core).ShouldNotContain(
            "DiffHacker.Host",
            "DiffHacker.Core must not depend on the application shell (CLAUDE.md §0.3).");
    }

    [Fact]
    public void Core_does_not_reference_Microsoft_Extensions_AI()
    {
        // §0.3: the LLM abstraction must not leak out of DiffHacker.Llm into the domain.
        ReferencedAssemblyNames(Core)
            .ShouldNotContain(name => name.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void No_domain_assembly_references_Photino(Assembly assembly)
    {
        ReferencedAssemblyNames(assembly)
            .ShouldNotContain(
                name => name.StartsWith("Photino", StringComparison.Ordinal),
                $"{assembly.GetName().Name} must not depend on the windowing library (CLAUDE.md §0.3).");
    }

    private static string[] ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();
}
