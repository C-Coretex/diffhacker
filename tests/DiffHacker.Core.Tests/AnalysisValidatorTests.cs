using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Tests;

/// <summary>
/// The most important tests in the project, by Iteration 7's own reckoning (requirement 9).
/// <para>
/// Each one starts from a result that passes, breaks exactly one thing, and asserts two separate
/// claims: that validation failed, and that the diagnostic <i>names the offending id or path</i>.
/// The second is the one that matters. A validator that rejects a bad result without saying which
/// file it dropped costs a repair round and teaches the model nothing, and requirement 4 is
/// specifically that the failure fed back is specific.
/// </para>
/// </summary>
public sealed class AnalysisValidatorTests
{
    [Fact]
    public void A_well_formed_result_passes_with_no_diagnostics_at_all()
    {
        var validation = AnalysisFixtures.Check(AnalysisFixtures.Valid());

        validation.IsValid.ShouldBeTrue();
        validation.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Every_changed_file_is_covered_and_the_check_is_set_equality_not_a_count()
    {
        // §0.2.5 as an assertion rather than a spot check: the set of paths the changeset holds and
        // the set the nodes cover are compared whole, so a result that dropped one file and
        // invented another could not pass by counting to three.
        var changeset = AnalysisFixtures.Changeset();
        var result = AnalysisFixtures.Valid();

        var covered = result.Nodes.Select(static node => node.FilePath).ToHashSet(StringComparer.Ordinal);
        var changed = changeset.Select(static file => file.Path).ToHashSet(StringComparer.Ordinal);

        covered.SetEquals(changed).ShouldBeTrue();
    }

    [Fact]
    public void A_changed_file_with_no_node_is_named_in_the_diagnostic()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes = [.. result.Nodes.Where(static node => node.FilePath != AnalysisFixtures.IconPath)],
            Containers = [.. result.Containers.Where(static container => container.Id != "removed-assets")],
            ReadingOrder = [AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        var diagnostic = validation.Errors
            .ShouldHaveSingleItem();

        diagnostic.Code.ShouldBe(AnalysisDiagnosticCodes.FileNotCovered);
        diagnostic.Subject.ShouldBe(AnalysisFixtures.IconPath);
        diagnostic.Message.ShouldContain(AnalysisFixtures.IconPath);
    }

    [Fact]
    public void A_node_for_a_file_that_did_not_change_is_rejected_rather_than_accepted_as_extra()
    {
        // The other half of completeness. A model that cannot find something to say about a file
        // could otherwise cover its own gap by inventing a neighbouring one.
        var result = AnalysisFixtures.Valid();
        var invented = AnalysisFixtures.Node("src/NeverTouched.cs", rank: 3);

        var broken = result with
        {
            Nodes = [.. result.Nodes, invented],
            Containers =
            [
                result.Containers[0] with
                {
                    NodeIds = [.. result.Containers[0].NodeIds, invented.Id],
                },
                result.Containers[1],
            ],
            ReadingOrder = [.. result.ReadingOrder, invented.Id],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();
        validation.Errors.ShouldContain(d =>
            d.Code == AnalysisDiagnosticCodes.UnknownFile && d.Message.Contains("src/NeverTouched.cs"));
    }

    [Fact]
    public void A_node_in_two_containers_is_named_along_with_both_of_them()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Containers =
            [
                result.Containers[0],
                result.Containers[1] with
                {
                    NodeIds = [AnalysisFixtures.IconPath, AnalysisFixtures.CallerPath],
                },
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        validation.Errors.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.NodeInManyContainers);

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.NodeInManyContainers);

        diagnostic.Subject.ShouldBe(AnalysisFixtures.CallerPath);
        diagnostic.Message.ShouldContain("contract-and-caller");
        diagnostic.Message.ShouldContain("removed-assets");
    }

    [Fact]
    public void A_node_in_no_container_is_named()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Containers =
            [
                result.Containers[0] with { NodeIds = [AnalysisFixtures.ContractPath] },
                result.Containers[1],
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        validation.Errors.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.NodeInNoContainer);

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.NodeInNoContainer);

        diagnostic.Subject.ShouldBe(AnalysisFixtures.CallerPath);
    }

    [Fact]
    public void An_edge_to_a_node_that_does_not_exist_names_the_edge_and_the_missing_end()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Edges =
            [
                result.Edges[0] with { TargetNodeId = "src/Ghost.cs" },
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        validation.Errors.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.DanglingEdge);

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.DanglingEdge);

        diagnostic.Message.ShouldContain("src/Ghost.cs");
        diagnostic.Message.ShouldContain("target");
        diagnostic.Subject.ShouldContain(AnalysisFixtures.ContractPath);
    }

    [Fact]
    public void A_container_with_no_entry_node_is_named()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Containers =
            [
                result.Containers[0] with { EntryNodeId = string.Empty },
                result.Containers[1],
            ],
            Nodes =
            [
                AnalysisFixtures.Node(AnalysisFixtures.ContractPath, rank: 1, importance: 5),
                result.Nodes[1],
                result.Nodes[2],
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        validation.Errors.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.NoEntryNode);

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.NoEntryNode);

        diagnostic.Subject.ShouldBe("contract-and-caller");
    }

    [Fact]
    public void A_container_with_two_entry_nodes_names_both_of_them()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes =
            [
                result.Nodes[0],
                AnalysisFixtures.Node(
                    AnalysisFixtures.CallerPath,
                    rank: 2,
                    states: [AnalysisNodeState.Changed, AnalysisNodeState.EntryPoint]),
                result.Nodes[2],
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        validation.Errors.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.ManyEntryNodes);

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.ManyEntryNodes);

        diagnostic.Subject.ShouldBe("contract-and-caller");
        diagnostic.Message.ShouldContain(AnalysisFixtures.ContractPath);
        diagnostic.Message.ShouldContain(AnalysisFixtures.CallerPath);
    }

    [Fact]
    public void An_entry_node_that_is_not_a_member_of_its_container_is_named()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Containers =
            [
                result.Containers[0] with { EntryNodeId = AnalysisFixtures.IconPath },
                result.Containers[1],
            ],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.EntryNodeNotAMember &&
                d.Message.Contains(AnalysisFixtures.IconPath));
    }

    [Fact]
    public void An_entry_node_that_is_not_ranked_first_is_named_with_the_rank_it_has()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes =
            [
                result.Nodes[0] with { Rank = 2 },
                result.Nodes[1] with { Rank = 1 },
                result.Nodes[2],
            ],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.EntryNodeNotFirst && d.Message.Contains("rank 2"));
    }

    [Fact]
    public void Duplicate_node_ids_are_named()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes =
            [
                result.Nodes[0],
                result.Nodes[1] with { Id = AnalysisFixtures.ContractPath },
                result.Nodes[2],
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        validation.Errors.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.DuplicateNodeId);

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.DuplicateNodeId);

        diagnostic.Subject.ShouldBe(AnalysisFixtures.ContractPath);
    }

    [Fact]
    public void Ranks_with_a_gap_are_reported_with_the_values_that_were_given()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes =
            [
                result.Nodes[0],
                result.Nodes[1] with { Rank = 7 },
                result.Nodes[2],
            ],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.RankNotDense &&
                d.Message.Contains("contract-and-caller") &&
                d.Message.Contains("1, 7"));
    }

    [Fact]
    public void Container_display_orders_that_repeat_are_reported()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Containers =
            [
                result.Containers[0],
                result.Containers[1] with { DisplayOrder = 1 },
            ],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d => d.Code == AnalysisDiagnosticCodes.DisplayOrderNotDense);
    }

    [Fact]
    public void A_cycle_is_reported_as_a_warning_and_does_not_fail_the_run()
    {
        // The decision recorded in the plan: mutual dependencies are real, and rejecting them would
        // be asking the model to misdescribe the repository. Reading direction survives because it
        // comes from container order and rank, not from following edges.
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Edges =
            [
                result.Edges[0],
                new AnalysisEdge
                {
                    SourceNodeId = AnalysisFixtures.CallerPath,
                    TargetNodeId = AnalysisFixtures.ContractPath,
                    Kind = AnalysisEdgeKind.Conceptual,
                    Explanation = "And back again.",
                    Risks = [],
                },
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeTrue();

        validation.Warnings.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.Cycle);

        var warning = validation.Warnings.First(d => d.Code == AnalysisDiagnosticCodes.Cycle);

        warning.Message.ShouldContain(AnalysisFixtures.ContractPath);
        warning.Message.ShouldContain(AnalysisFixtures.CallerPath);
    }

    [Fact]
    public void A_node_nothing_leads_to_is_reported_as_a_gap_in_the_reading_path()
    {
        // The closest thing to a check on what the product is for: a reviewer walks from the entry
        // node outwards, and a node no edge reaches is one they arrive at cold.
        var result = AnalysisFixtures.Valid();
        var broken = result with { Edges = [] };

        var validation = AnalysisFixtures.Check(broken);

        // A warning, not a failure: the node may belong there with the connection left unsaid.
        validation.IsValid.ShouldBeTrue();

        validation.Warnings.Count(d => d.Code == AnalysisDiagnosticCodes.UnreachableNode).ShouldBe(1);

        var diagnostic = validation.Warnings.First(d => d.Code == AnalysisDiagnosticCodes.UnreachableNode);

        diagnostic.Subject.ShouldBe(AnalysisFixtures.CallerPath);
        diagnostic.Message.ShouldContain(AnalysisFixtures.ContractPath);
        diagnostic.Message.ShouldContain("contract-and-caller");
    }

    [Fact]
    public void A_conceptual_edge_makes_a_node_reachable_just_as_a_direct_one_does()
    {
        // The point the product turns on: a cluster held together by intent alone, with no code
        // dependency between its members, is a real cluster and a walkable one.
        var result = AnalysisFixtures.Valid();

        var conceptual = result with
        {
            Edges =
            [
                result.Edges[0] with { Kind = AnalysisEdgeKind.Conceptual },
            ],
        };

        AnalysisFixtures.Check(conceptual).Warnings
            .ShouldNotContain(d => d.Code == AnalysisDiagnosticCodes.UnreachableNode);
    }

    [Fact]
    public void A_cross_container_edge_does_not_count_as_a_way_into_a_cluster()
    {
        // Cross-container edges are drawn faintly and kept out of the layout (§0.6), so they are not
        // something a reader follows to arrive somewhere. A cluster reachable only from outside
        // itself still has a gap in it.
        var result = AnalysisFixtures.Valid();

        var outside = result with
        {
            Edges =
            [
                new AnalysisEdge
                {
                    SourceNodeId = AnalysisFixtures.IconPath,
                    TargetNodeId = AnalysisFixtures.CallerPath,
                    Kind = AnalysisEdgeKind.Conceptual,
                    Explanation = "From one cluster into another.",
                    Risks = [],
                },
            ],
        };

        AnalysisFixtures.Check(outside).Warnings
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.UnreachableNode &&
                d.Subject == AnalysisFixtures.CallerPath);
    }

    [Fact]
    public void A_container_holding_only_its_entry_node_has_no_path_to_report_on()
    {
        // The icon's container in the fixture. One node is already the whole walk.
        AnalysisFixtures.Check(AnalysisFixtures.Valid()).Warnings
            .ShouldNotContain(d => d.Subject == AnalysisFixtures.IconPath);
    }

    [Fact]
    public void A_node_id_that_is_not_derived_from_its_path_is_rejected()
    {
        // §0.6 keys reviewed-state off node ids, so an id the model invented freely would reset
        // every time it phrased something differently.
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes =
            [
                result.Nodes[0] with { Id = "node-1" },
                result.Nodes[1],
                result.Nodes[2],
            ],
            Containers =
            [
                result.Containers[0] with
                {
                    EntryNodeId = "node-1",
                    NodeIds = ["node-1", AnalysisFixtures.CallerPath],
                },
                result.Containers[1],
            ],
            ReadingOrder = ["node-1", AnalysisFixtures.CallerPath, AnalysisFixtures.IconPath],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.NodeIdNotDerived &&
                d.Message.Contains(AnalysisFixtures.ContractPath));
    }

    [Fact]
    public void Two_nodes_for_one_file_are_allowed_when_the_second_carries_a_slug()
    {
        // §0.6 permits the split for two genuinely unrelated changes in one file. Validation has to
        // allow it, or the invariant and the rule would contradict each other.
        var result = AnalysisFixtures.Valid();
        var second = AnalysisFixtures.Node(
            AnalysisFixtures.CallerPath,
            rank: 3,
            id: AnalysisFixtures.CallerPath + "#logging");

        var split = result with
        {
            Nodes = [.. result.Nodes, second],
            Containers =
            [
                result.Containers[0] with
                {
                    NodeIds = [.. result.Containers[0].NodeIds, second.Id],
                },
                result.Containers[1],
            ],
            ReadingOrder = [.. result.ReadingOrder, second.Id],
        };

        AnalysisFixtures.Check(split).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_thin_node_for_a_deleted_binary_is_not_a_failure()
    {
        // The icon in the fixture is exactly this: deleted, binary, and with nothing to say about
        // downstream effects or implementation detail. Requiring more would make the completeness
        // invariant unsatisfiable for the files it most needs to cover.
        var icon = AnalysisFixtures.Valid().Nodes.Single(static n => n.FilePath == AnalysisFixtures.IconPath);

        icon.HowItAffectsOthers.ShouldBeEmpty();
        icon.ImplementationNotes.ShouldBeEmpty();
        icon.Risks.ShouldBeEmpty();

        AnalysisFixtures.Check(AnalysisFixtures.Valid()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_node_with_an_empty_whyItChanged_is_rejected_and_the_field_is_named()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes = [result.Nodes[0] with { WhyItChanged = "  " }, result.Nodes[1], result.Nodes[2]],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.EmptyNodeField && d.Message.Contains("whyItChanged"));
    }

    [Fact]
    public void An_importance_outside_one_to_five_is_reported_with_the_value_given()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes = [result.Nodes[0] with { Importance = 9 }, result.Nodes[1], result.Nodes[2]],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.ImportanceOutOfRange && d.Message.Contains("importance 9"));
    }

    [Fact]
    public void A_reading_order_naming_a_node_twice_is_rejected()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            ReadingOrder = [AnalysisFixtures.ContractPath, AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d => d.Code == AnalysisDiagnosticCodes.DuplicateReadingOrderEntry);
    }

    [Fact]
    public void An_incomplete_reading_order_is_a_warning_because_rank_already_answers_it()
    {
        var result = AnalysisFixtures.Valid();
        var broken = result with { ReadingOrder = [AnalysisFixtures.ContractPath] };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeTrue();
        validation.Warnings.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.IncompleteReadingOrder);

        // And the traversal offered to the reviewer is the derived one, covering everything.
        AnalysisReadingOrder.Resolve(broken).ShouldBe(
            [AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath, AnalysisFixtures.IconPath]);
    }

    [Fact]
    public void Every_error_message_names_something_a_model_can_act_on()
    {
        // A blanket check over the whole taxonomy: whatever else a diagnostic says, it quotes its
        // subject, so "fix this" is never advice without an address.
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes = [result.Nodes[0] with { Importance = 0 }, result.Nodes[1] with { Rank = 9 }, result.Nodes[2]],
            Edges = [result.Edges[0] with { SourceNodeId = "nowhere" }],
        };

        var errors = AnalysisFixtures.Check(broken).Errors;

        errors.ShouldNotBeEmpty();
        errors.ShouldAllBe(d => d.Message.Length > 20);
        errors.Where(d => d.Subject.Length > 0).ShouldAllBe(d => d.Message.Contains(d.Subject));
    }

    [Fact]
    public void An_empty_changeset_and_an_empty_result_agree_with_each_other()
    {
        var empty = new AnalysisResult { Summary = "Nothing changed." };

        AnalysisValidator.Validate(empty, []).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_field_that_runs_far_past_its_budget_is_a_warning_and_never_an_error()
    {
        // Iteration 8 requirement 15. The model was asked for a length; overshooting is worth
        // recording and is never worth a repair round on a three-hundred-node document. Making this
        // an error — or a maxLength in the schema, which the provider would enforce — would have the
        // whole answer rewritten because one sentence ran long.
        var result = AnalysisFixtures.Valid();
        var verbose = result with
        {
            Nodes =
            [
                result.Nodes[0] with { Title = new string('x', AnalysisFieldBudgets.NodeTitle * 3) },
                result.Nodes[1],
                result.Nodes[2],
            ],
        };

        var validation = AnalysisFixtures.Check(verbose);

        validation.IsValid.ShouldBeTrue("a long sentence is not a reason to reject an answer.");
        validation.Warnings.ShouldContain(d =>
            d.Code == AnalysisDiagnosticCodes.VerboseField && d.Subject == result.Nodes[0].Id);
    }

    [Fact]
    public void A_field_a_little_over_its_budget_is_left_alone()
    {
        // The budgets are what fits comfortably, not a hard edge. A diagnostic on every slight
        // overshoot is one nobody reads, so nothing is said until twice the length.
        var result = AnalysisFixtures.Valid();
        var slightlyLong = result with
        {
            Nodes =
            [
                result.Nodes[0] with { Title = new string('x', AnalysisFieldBudgets.NodeTitle + 20) },
                result.Nodes[1],
                result.Nodes[2],
            ],
        };

        AnalysisFixtures.Check(slightlyLong).Warnings
            .ShouldNotContain(d => d.Code == AnalysisDiagnosticCodes.VerboseField);
    }

    [Fact]
    public void An_overlong_summary_risk_and_container_field_are_all_reported()
    {
        // Every written field has a budget, not only the ones on a node.
        var result = AnalysisFixtures.Valid();
        var verbose = result with
        {
            Summary = new string('x', AnalysisFieldBudgets.OverallSummary * 3),
            OverallRisks = [new string('x', AnalysisFieldBudgets.Risk * 3)],
            Containers =
            [
                result.Containers[0] with
                {
                    Explanation = new string('x', AnalysisFieldBudgets.ContainerExplanation * 3),
                },
                result.Containers[1],
            ],
        };

        var warnings = AnalysisFixtures.Check(verbose).Warnings
            .Where(d => d.Code == AnalysisDiagnosticCodes.VerboseField)
            .ToList();

        warnings.Count.ShouldBe(3);

        // The two about the change as a whole carry no subject; the container one names itself, so
        // a reader knows what to shorten.
        warnings.Count(d => d.Subject.Length == 0).ShouldBe(2);
        warnings.ShouldContain(d => d.Subject == result.Containers[0].Id);
    }

    [Fact]
    public void A_result_describing_a_changeset_it_was_not_given_fails_on_every_file()
    {
        var result = AnalysisFixtures.Valid();
        var other = new[] { AnalysisFixtures.File("src/Elsewhere.cs") };

        var validation = AnalysisValidator.Validate(result, other);

        validation.IsValid.ShouldBeFalse();
        validation.Errors.Count(static d => d.Code == AnalysisDiagnosticCodes.UnknownFile).ShouldBe(3);
        validation.Errors.ShouldContain(d =>
            d.Code == AnalysisDiagnosticCodes.FileNotCovered && d.Subject == "src/Elsewhere.cs");
    }
}
