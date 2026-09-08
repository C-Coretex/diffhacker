using System.Text.RegularExpressions;
using DiffHacker.Core.Knowledge;

namespace DiffHacker.Core.Tests;

/// <summary>
/// The prompt, and the documentation list inside it.
/// <para>
/// Requirement 2 says the profile builder reads the repository's own documentation before it
/// explores code. A general instruction to do that is drifted from; a list of paths in front of the
/// model is not. So the list is part of the contract and is tested as one.
/// </para>
/// </summary>
public sealed class ProfilePromptTests
{
    [Fact]
    public void Documentation_is_found_by_name_and_by_directory()
    {
        var found = ProfilePrompt.FindDocumentation(
        [
            "README.md",
            "ARCHITECTURE.md",
            "CONTRIBUTING.md",
            "CLAUDE.md",
            "AGENTS.md",
            "docs/decisions.md",
            "doc/adr/0001-use-sqlite.md",
            "src/Program.cs",
            "package.json",
        ]);

        found.ShouldContain("README.md");
        found.ShouldContain("ARCHITECTURE.md");
        found.ShouldContain("CONTRIBUTING.md");
        found.ShouldContain("CLAUDE.md");
        found.ShouldContain("AGENTS.md");
        found.ShouldContain("docs/decisions.md");
        found.ShouldContain("doc/adr/0001-use-sqlite.md");
    }

    [Fact]
    public void Source_files_and_manifests_are_not_documentation()
    {
        var found = ProfilePrompt.FindDocumentation(["src/Program.cs", "package.json", "go.mod"]);

        found.ShouldBeEmpty();
    }

    [Fact]
    public void A_readme_with_no_extension_still_counts()
    {
        ProfilePrompt.FindDocumentation(["README"]).ShouldContain("README");
    }

    [Fact]
    public void Shallow_documentation_comes_before_deep_documentation()
    {
        // The list is truncated, and the top of a repository explains it better than the bottom.
        var found = ProfilePrompt.FindDocumentation(["docs/a/b/c/deep.md", "README.md", "docs/overview.md"]);

        found[0].ShouldBe("README.md");
        found[1].ShouldBe("docs/overview.md");
    }

    [Fact]
    public void The_list_is_capped_so_a_documentation_heavy_repository_cannot_flood_the_prompt()
    {
        var many = Enumerable.Range(0, 500).Select(i => $"docs/page-{i:000}.md");

        ProfilePrompt.FindDocumentation(many).Count.ShouldBeLessThanOrEqualTo(40);
    }

    [Fact]
    public void The_opening_message_names_the_documentation_and_orders_it_read_first()
    {
        var message = ProfilePrompt.OpeningMessage(
            "diffhacker", 1200, ["README.md", "CLAUDE.md"], string.Empty);

        message.ShouldContain("diffhacker");
        message.ShouldContain("1,200");
        message.ShouldContain("- README.md");
        message.ShouldContain("- CLAUDE.md");
        message.ShouldContain("before any");
    }

    [Fact]
    public void A_repository_with_no_documentation_is_told_so_rather_than_shown_an_empty_list()
    {
        var message = ProfilePrompt.OpeningMessage("bare", 12, [], string.Empty);

        message.ShouldContain("no documentation files");
        message.ShouldContain("get_repository_tree");
    }

    [Fact]
    public void Custom_instructions_are_carried_and_marked_as_overriding()
    {
        var message = ProfilePrompt.OpeningMessage(
            "repo", 10, ["README.md"], "Ignore the generated/ folder.");

        message.ShouldContain("Ignore the generated/ folder.");
        message.ShouldContain("override");
    }

    [Fact]
    public void The_opening_message_carries_no_file_content()
    {
        // §0.2.9: the prompt names files and instructs. Anything that looked like content here
        // would be the invariant breaking on its first real prompt.
        var message = ProfilePrompt.OpeningMessage("repo", 10, ["README.md"], string.Empty);

        message.Length.ShouldBeLessThan(2000);
    }

    [Fact]
    public void The_system_prompt_states_the_budget_and_the_reading_order()
    {
        // Whitespace-collapsed, because the prompt is a wrapped raw string literal and a phrase
        // that happens to straddle a line break is not a different phrase.
        var prompt = Regex.Replace(ProfilePrompt.SystemPrompt(12_000), @"\s+", " ");

        prompt.ShouldContain("12,000");
        prompt.ShouldContain("get_project_profile");
        prompt.ShouldContain("documentation before any code");
        prompt.ShouldContain("report_progress");
        prompt.ShouldContain("Older tool results may be pruned");
        prompt.ShouldContain("Only tool results are pruned");
        prompt.ShouldContain("state it clearly and concisely in your reasoning");
        prompt.ShouldContain("call the tool again");
    }

    [Fact]
    public void The_repair_message_names_both_numbers()
    {
        var repair = ProfilePrompt.OverBudgetRepair(15_400, 12_000);

        repair.ShouldContain("15,400");
        repair.ShouldContain("12,000");

        // "Shorten it" with no target is a request a model satisfies by trimming one sentence.
        repair.ShouldContain("Do not drop a section");
    }
}
