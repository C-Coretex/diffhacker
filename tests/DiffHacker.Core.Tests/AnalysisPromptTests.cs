using System.Globalization;
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
        prompt.ShouldContain("reachable from its container's entry node");
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
        prompt.ShouldContain("first entry of its nodeIds");
        prompt.ShouldContain("no gaps and no repeats");
        prompt.ShouldContain("reading order lists every node once");
        prompt.ShouldContain("lists no node twice");

        // Iteration 11: those five are checked once per grouping, and the prompt says so where a
        // model can act on it rather than after the answer comes back.
        prompt.ShouldContain("once for EACH grouping");
    }

    [Fact]
    public void The_prompt_asks_for_two_groupings_and_says_what_each_one_is_for()
    {
        // Iteration 11's whole difficulty is that the two groupings conflict, so the prompt has to
        // state the conflict rather than describe grouping twice and hope for two answers.
        var prompt = Prompt();

        prompt.ShouldContain("GROUP THE SAME NODES TWICE");
        prompt.ShouldContain("dependencyContainers");
        prompt.ShouldContain("clusterContainers");
        prompt.ShouldContain("Same nodes, same explanations, same edges");
    }

    [Fact]
    public void The_prompt_says_a_dependency_path_stays_whole_even_across_concerns()
    {
        // The defining property of the default grouping, and the thing a model will otherwise undo:
        // splitting a path at a concern boundary is exactly what the other grouping is for.
        var prompt = Prompt();

        prompt.ShouldContain("keep a COMPLETE change path");
        prompt.ShouldContain("spans database, auth and API");
        prompt.ShouldContain("Do not cut a path because it crosses concerns");
    }

    [Fact]
    public void The_prompt_says_change_clusters_is_a_real_view_and_not_a_consolation_prize()
    {
        // The iteration text is explicit that the second grouping is "a genuinely good complementary
        // view, not a consolation prize". A model that treats it as a rough draft produces the first
        // grouping with the labels changed, which is worth nothing to switch to.
        var prompt = Prompt();

        prompt.ShouldContain("not a fallback and not a rough draft");
        prompt.ShouldContain("what areas did this touch");
        prompt.ShouldContain("has been given nothing");
    }

    [Fact]
    public void A_run_that_wants_one_grouping_is_never_told_about_the_other()
    {
        // The opt-out is only worth having if it takes the work out of the request. A prompt that
        // described the second grouping and then forbade it would cost the tokens of the
        // description on every one of up to three hundred turns.
        var prompt = Prompt(changeClusters: false);

        prompt.ShouldNotContain("clusterContainers");
        prompt.ShouldNotContain("clusterReadingOrder");
        prompt.ShouldNotContain("GROUP THE SAME NODES TWICE");

        // And it still says the thing dependency flow is for, plus that the other view is not
        // wanted — so a model does not compromise between two groupings it was asked for one of.
        prompt.ShouldContain("keep a COMPLETE change path");
        prompt.ShouldContain("not wanted on this run");
    }

    [Fact]
    public void Dropping_the_second_grouping_makes_the_request_measurably_smaller()
    {
        // Measured rather than assumed, because this is the only reason the opt-out exists. The
        // schema shrinks too — see AnalysisResponseSchemaTests — and both halves are re-sent every
        // turn, the schema twice over.
        var both = AnalysisPrompt.SystemPrompt(changeClusters: true).Length;
        var one = AnalysisPrompt.SystemPrompt(changeClusters: false).Length;

        one.ShouldBeLessThan(both);
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
    public void The_prompt_says_why_the_answer_has_to_be_short_and_not_only_that_it_does()
    {
        // Iteration 8 requirement 15. A model told "be concise" writes the same paragraph with
        // fewer adjectives; a model told where the text lands writes a label instead.
        var prompt = Prompt();

        prompt.ShouldContain("the size of a business card");
        prompt.ShouldContain("three hundred others beside it");
        prompt.ShouldContain("Cut the preamble, not the content");
    }

    [Fact]
    public void The_prompt_states_every_length_budget_the_validator_measures_against()
    {
        // Three readers of one set of numbers: this prompt, AnalysisValidator's warning, and the
        // renderer's truncation. A budget changed in AnalysisFieldBudgets and not here would have
        // the model asked for one length and reported against another.
        var prompt = Prompt();

        foreach (var budget in new[]
        {
            AnalysisFieldBudgets.NodeTitle,
            AnalysisFieldBudgets.NodeProse,
            AnalysisFieldBudgets.ContainerTitle,
            AnalysisFieldBudgets.ContainerSummary,
            AnalysisFieldBudgets.ContainerExplanation,
            AnalysisFieldBudgets.OverallSummary,
            AnalysisFieldBudgets.Risk,
        })
        {
            prompt.ShouldContain(
                budget.ToString(CultureInfo.InvariantCulture),
                Case.Sensitive,
                $"the prompt has to state the {budget}-character budget the validator measures against");
        }
    }

    [Fact]
    public void The_length_budgets_are_asked_for_rather_than_enforced()
    {
        // The distinction requirement 15 lives or dies on. A model that believes overshooting is
        // fatal drops facts to fit, which is worse than a long sentence — and the schema carries no
        // maxLength precisely so that overshooting costs nothing.
        var prompt = Prompt();

        prompt.ShouldContain("Going over is not rejected");
        prompt.ShouldContain("never drop a fact to fit");
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
    private static string Prompt(bool changeClusters = true) =>
        WhitespaceRuns().Replace(AnalysisPrompt.SystemPrompt(changeClusters), " ");

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
