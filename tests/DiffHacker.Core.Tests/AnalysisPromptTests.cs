using System.Text.RegularExpressions;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Knowledge;

namespace DiffHacker.Core.Tests;

/// <summary>
/// What the model is told, and what it is never told.
/// <para>
/// The prompt is the product here in a way that is easy to lose. Everything downstream of it — the
/// validator, the store, the screen — only decides whether a graph is well formed; whether the graph
/// is any <i>use</i> is decided by these words. So the guidance that carries the product's whole
/// point is pinned: a reading path with a starting point, clusters held together by intent rather
/// than by imports, and relationships that need no code behind them to be worth drawing.
/// </para>
/// <para>
/// Pinned by the ideas they express rather than by their wording, so the prose can be improved
/// without the test having to be rewritten to match — but not silently dropped.
/// </para>
/// <para>
/// Every assertion runs against <see cref="Prompt"/>, which collapses runs of whitespace to single
/// spaces. The prompt is a wrapped raw string literal, and a phrase that happens to straddle a line
/// break is not a different phrase — without this, rewrapping a paragraph fails a test for no
/// reason and the fix is to contort the prose, which is exactly backwards.
/// </para>
/// </summary>
public sealed partial class AnalysisPromptTests
{
    [Fact]
    public void The_prompt_says_the_answer_is_a_reading_path_with_a_starting_point()
    {
        var prompt = Prompt();

        prompt.ShouldContain("starting point");
        prompt.ShouldContain("reading path");

        // The test of a good ordering, stated as the reader's experience rather than as a rule.
        prompt.ShouldContain("make sense given only the nodes before it");
    }

    [Fact]
    public void The_prompt_says_plainly_that_this_is_not_a_dependency_graph()
    {
        // The failure mode this product exists to avoid: a model that maps imports and calls, and
        // leaves the reviewer holding everything a compiler cannot see.
        var prompt = Prompt();

        prompt.ShouldContain("THIS IS NOT A DEPENDENCY GRAPH");
        prompt.ShouldContain("no code between");
        prompt.ShouldContain("could prove with a grep");
    }

    [Fact]
    public void The_prompt_says_a_cluster_can_be_held_together_by_intent_alone()
    {
        var prompt = Prompt();

        prompt.ShouldContain("not the same as a code dependency");
        prompt.ShouldContain("one decision");

        // And names the ways a model most often gets grouping wrong.
        prompt.ShouldContain("Not a directory");
        prompt.ShouldContain("Not a leftovers bin");
    }

    [Fact]
    public void The_prompt_explains_how_to_choose_an_entry_node_rather_than_only_requiring_one()
    {
        var prompt = Prompt();

        prompt.ShouldContain("EXPLAINS the others");
        prompt.ShouldContain("roughly predictable");
        prompt.ShouldContain("not the biggest file");
    }

    [Fact]
    public void The_prompt_gives_rank_a_criterion_and_not_only_a_shape()
    {
        // Without this, "1, 2, 3 with no gaps" is satisfied by any order at all, and a model will
        // reach for importance or path order — which produces a pile, not a walk.
        var prompt = Prompt();

        prompt.ShouldContain("NOT by importance and NOT by size");
        prompt.ShouldContain("everything a reader needs in order to understand it");
    }

    [Fact]
    public void The_prompt_treats_conceptual_edges_as_wanted_rather_than_tolerated()
    {
        var prompt = Prompt();

        prompt.ShouldContain("not weaker");
        prompt.ShouldContain("not a last resort");
        prompt.ShouldContain("reachable from that container's entry node");
    }

    [Fact]
    public void The_prompt_states_every_rule_the_validator_will_enforce()
    {
        // A model told afterwards has already spent the run, and every repair round costs real
        // money on a large change. Each of these maps to a check in AnalysisValidator.
        var prompt = Prompt();

        prompt.ShouldContain("EVERY changed file has at least one node");
        prompt.ShouldContain("No node names a file that is not in the changed-file list");
        prompt.ShouldContain("exactly one container");
        prompt.ShouldContain("exactly one entry node");
        prompt.ShouldContain("no gaps and no repeats");
        prompt.ShouldContain("reading order lists every node once");
    }

    [Fact]
    public void The_prompt_warns_that_older_tool_results_can_be_pruned()
    {
        var prompt = Prompt();

        prompt.ShouldContain("Older tool results may be pruned");
        prompt.ShouldContain("Only tool results are pruned");
        prompt.ShouldContain("state it clearly and concisely in your reasoning");
        prompt.ShouldContain("call the tool again");
    }

    [Fact]
    public void The_opening_message_carries_the_file_list_and_no_file_contents()
    {
        // §0.2.9, checked where the prompt is built rather than only end to end.
        var message = AnalysisPrompt.OpeningMessage("fixture", Changeset(), null);

        message.ShouldContain("src/Contract.cs");
        message.ShouldContain("assets/icon.png");
        message.ShouldContain("M +12 -3");

        message.ShouldNotContain("public sealed record Contract");
        message.ShouldNotContain("@@");
        message.ShouldNotContain("+++ b/");
    }

    [Fact]
    public void The_opening_message_carries_the_profile_and_the_standing_instructions()
    {
        var profile = new ProjectProfile
        {
            RepositoryPath = "/repo",
            CustomInstructions = "Ignore the generated folder.",
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            Document = new ProjectProfileDocument
            {
                Purpose = "A desktop reviewer for large diffs.",
                Architecture = "A host and a renderer.",
                Layering = "Core knows nothing of the host.",
                Patterns = "Contracts are generated.",
                TestLayout = "xUnit beside each project.",
            },
        };

        var message = AnalysisPrompt.OpeningMessage("fixture", Changeset(), profile);

        message.ShouldContain("A desktop reviewer for large diffs.");
        message.ShouldContain("Ignore the generated folder.");
    }

    [Fact]
    public void Without_a_profile_the_model_is_told_to_explore_more_carefully()
    {
        // §0.6 makes the profile skippable with a warning that results are weaker. This is the
        // model's half of that warning.
        var message = AnalysisPrompt.OpeningMessage("fixture", Changeset(), null);

        message.ShouldContain("No project profile has been generated");
        message.ShouldContain("say plainly where you are inferring");
    }

    [Fact]
    public void The_changed_file_list_is_spelled_the_way_the_toolbox_spells_it()
    {
        // One format, two consumers: this list and list_changed_files. A model told two different
        // things about one file in one conversation has to reconcile them, and should never have
        // been asked to.
        var file = AnalysisFixtures.File("src/Contract.cs", ChangeStatus.Modified, 12, 3);
        var message = AnalysisPrompt.OpeningMessage("fixture", Changeset(), null);

        message.ShouldContain(ChangedFileText.Row(file));
        message.ShouldContain(ChangedFileText.Legend);
    }

    /// <summary>
    /// The system prompt with runs of whitespace collapsed, so an assertion matches a phrase
    /// wherever the paragraph happens to wrap.
    /// </summary>
    private static string Prompt() =>
        WhitespaceRuns().Replace(AnalysisPrompt.SystemPrompt(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();

    private static Changeset Changeset()
    {
        var files = AnalysisFixtures.Changeset();

        return new Changeset
        {
            RepositoryPath = "/repo",
            IsClean = false,
            HasCommits = true,
            UntrackedIncluded = true,
            Files = files,
            Statistics = ChangesetStatistics.From(files),
            HunkCountsAvailable = true,
        };
    }
}
