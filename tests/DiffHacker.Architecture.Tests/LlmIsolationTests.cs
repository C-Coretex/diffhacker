namespace DiffHacker.Architecture.Tests;

/// <summary>
/// CLAUDE.md §0.3: "<c>Microsoft.Extensions.AI</c> types must not leak into
/// <c>DiffHacker.Core</c>."
/// <para>
/// <see cref="LayeringTests.Core_does_not_reference_Microsoft_Extensions_AI"/> already checks
/// the compiled assembly references, which is the stronger guarantee. It has one gap:
/// <c>GetReferencedAssemblies</c> reports only what the compiler actually emitted, so a
/// <c>using</c> that resolved to nothing observable would pass. This closes it at the source
/// level, the same way <see cref="PhotinoIsolationTests"/> does for the shell.
/// </para>
/// <para>
/// The rule matters more than it looks. <c>ILlmSession</c> is the seam that lets a provider be
/// swapped without touching analysis logic (§0.2.4), and the first MEAI type that reaches Core
/// is the point at which that stops being true.
/// </para>
/// </summary>
public sealed class LlmIsolationTests
{
    private const string CoreDirectory = "src/DiffHacker.Core/";

    [Fact]
    public void No_Core_source_file_mentions_Microsoft_Extensions_AI()
    {
        var offenders = RepositoryLayout.SourceFiles()
            .Select(RepositoryLayout.RelativePath)
            .Where(static relative => relative.StartsWith(CoreDirectory, StringComparison.Ordinal))
            .Where(relative => RepositoryLayout
                .CodeWithoutComments(Path.Combine(RepositoryLayout.Root, relative))
                .Contains("Microsoft.Extensions.AI", StringComparison.Ordinal))
            .ToArray();

        offenders.ShouldBeEmpty(
            "The LLM abstraction belongs to DiffHacker.Llm. Offending files: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_Core_source_file_names_a_provider_SDK()
    {
        // Same rule, one level out: an OpenAI or Anthropic type in Core would be
        // provider-specific knowledge in the layer that must not have any (§0.2.4), even
        // though it would not trip the MEAI check above.
        string[] forbidden = ["using OpenAI", "using Anthropic"];

        var offenders = RepositoryLayout.SourceFiles()
            .Select(RepositoryLayout.RelativePath)
            .Where(static relative => relative.StartsWith(CoreDirectory, StringComparison.Ordinal))
            .Where(relative =>
            {
                var text = RepositoryLayout.CodeWithoutComments(Path.Combine(RepositoryLayout.Root, relative));
                return forbidden.Any(token => text.Contains(token, StringComparison.Ordinal));
            })
            .ToArray();

        offenders.ShouldBeEmpty("Offending files: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_Core_directory_is_where_this_test_thinks_it_is()
    {
        // Guards against the rule passing vacuously because the project was renamed or moved.
        RepositoryLayout.SourceFiles()
            .Select(RepositoryLayout.RelativePath)
            .Any(static relative => relative.StartsWith(CoreDirectory, StringComparison.Ordinal))
            .ShouldBeTrue($"No source files were found under {CoreDirectory}.");
    }
}
