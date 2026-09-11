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
/// <para>
/// Since Iteration 11 the fixture holds two groupings, and the rules about clusters, entry points
/// and reading order are checked against each of them. A fault in the grouping nobody is looking at
/// still fails the run: the reviewer can switch to it with one click, and §0.2.5 is a promise about
/// both pictures.
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
    public void Both_groupings_cover_exactly_the_same_nodes()
    {
        // Iteration 11's verification step 1, at the document level: the two pictures are two
        // arrangements of one node set, so a node in one and not the other is not a different view —
        // it is a missing file in whichever the reviewer happens to open.
        var result = AnalysisFixtures.Valid();
        var nodes = result.Nodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var grouping in result.Groupings)
        {
            var members = result.For(grouping).Containers
                .SelectMany(static container => container.NodeIds)
                .ToHashSet(StringComparer.Ordinal);

            members.SetEquals(nodes).ShouldBeTrue($"the {grouping} grouping covers a different node set.");
        }
    }

    [Fact]
    public void A_changed_file_with_no_node_is_named_in_the_diagnostic()
    {
        var result = AnalysisFixtures.Valid();
        var broken = Without(result, AnalysisFixtures.IconPath);

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
        var invented = AnalysisFixtures.Node("src/NeverTouched.cs");
        var broken = With(result, invented);

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
            DependencyContainers =
            [
                result.DependencyContainers[0],
                result.DependencyContainers[1] with
                {
                    NodeIds = [AnalysisFixtures.IconPath, AnalysisFixtures.CallerPath],
                },
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

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
            DependencyContainers =
            [
                result.DependencyContainers[0] with { NodeIds = [AnalysisFixtures.ContractPath] },
                result.DependencyContainers[1],
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.NodeInNoContainer);

        diagnostic.Subject.ShouldBe(AnalysisFixtures.CallerPath);
    }

    [Fact]
    public void A_fault_in_the_grouping_nobody_is_looking_at_still_fails_the_run()
    {
        // Requirement 4: both modes obey the completeness invariant. The reviewer switches with one
        // click, so an answer whose second grouping drops a file is not half right.
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            ClusterContainers = [.. result.ClusterContainers.Where(static c => c.Id != "assets")],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.NodeInNoContainer);

        diagnostic.Subject.ShouldBe(AnalysisFixtures.IconPath);
        diagnostic.Grouping.ShouldBe(AnalysisGrouping.ChangeClusters);
    }

    [Fact]
    public void A_per_grouping_message_says_which_grouping_it_is_about()
    {
        // Two answers can hold a container called 'assets', so "container 'assets' has no nodes" is
        // not something a model can act on until it knows which of its two groupings that is.
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            ClusterContainers =
            [
                result.ClusterContainers[0],
                result.ClusterContainers[1],
                result.ClusterContainers[2] with { DisplayOrder = 1 },
            ],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.DisplayOrderNotDense &&
                d.Grouping == AnalysisGrouping.ChangeClusters &&
                d.Message.Contains("change-clusters grouping"));
    }

    [Fact]
    public void A_missing_second_grouping_fails_when_the_run_asked_for_it()
    {
        var validation = AnalysisFixtures.Check(AnalysisFixtures.DependencyOnly());

        validation.IsValid.ShouldBeFalse();
        validation.Errors.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.GroupingMissing);
    }

    [Fact]
    public void A_missing_second_grouping_is_not_a_finding_when_the_run_did_not_ask_for_it()
    {
        // Its fields were not in the schema the model answered, so its absence is what was asked
        // for rather than something to report.
        var validation = AnalysisFixtures.Check(
            AnalysisFixtures.DependencyOnly(),
            expectChangeClusters: false);

        validation.IsValid.ShouldBeTrue();
        validation.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void A_valid_implementation_group_passes_and_is_only_noted_where_it_is_split()
    {
        // Together in dependency flow, apart in change clusters: one warning, about the second
        // grouping only, naming the implementation that is drawn on its own there.
        var validation = AnalysisFixtures.Check(
            AnalysisFixtures.WithImplementationGroup(),
            expectImplementationGroups: true);

        validation.IsValid.ShouldBeTrue();

        var split = validation.Warnings.ShouldHaveSingleItem();

        split.Code.ShouldBe(AnalysisDiagnosticCodes.ImplementationGroupSplit);
        split.Subject.ShouldBe(AnalysisFixtures.CallerPath);
        split.Grouping.ShouldBe(AnalysisGrouping.ChangeClusters);
        split.Message.ShouldContain("change-clusters grouping");
        split.Message.ShouldContain(AnalysisFixtures.ContractPath);
    }

    [Fact]
    public void Missing_implementation_groups_fail_only_when_the_run_asked_for_them()
    {
        var unasked = AnalysisFixtures.Valid() with { ImplementationGroups = null };

        AnalysisFixtures.Check(unasked, expectImplementationGroups: true)
            .Errors.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.ImplementationGroupsMissing);

        AnalysisFixtures.Check(unasked, expectImplementationGroups: false)
            .IsValid.ShouldBeTrue();

        // An empty list is an answer: the model was asked and this change has none.
        AnalysisFixtures.Check(AnalysisFixtures.Valid(), expectImplementationGroups: true)
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void An_implementation_group_with_no_implementations_names_its_abstraction()
    {
        var broken = AnalysisFixtures.Valid() with
        {
            ImplementationGroups =
            [
                new AnalysisImplementationGroup { AbstractionNodeId = AnalysisFixtures.ContractPath },
            ],
        };

        var error = AnalysisFixtures.Check(broken).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(AnalysisDiagnosticCodes.ImplementationGroupEmpty);
        error.Subject.ShouldBe(AnalysisFixtures.ContractPath);
    }

    [Fact]
    public void An_implementation_group_naming_a_node_that_does_not_exist_names_it()
    {
        var broken = AnalysisFixtures.Valid() with
        {
            ImplementationGroups =
            [
                new AnalysisImplementationGroup
                {
                    AbstractionNodeId = AnalysisFixtures.ContractPath,
                    ImplementationNodeIds = ["src/Ghost.cs"],
                },
            ],
        };

        var error = AnalysisFixtures.Check(broken).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(AnalysisDiagnosticCodes.UnknownNodeReference);
        error.Message.ShouldContain("src/Ghost.cs");
    }

    [Fact]
    public void A_node_claimed_by_two_implementation_groups_is_named_with_both()
    {
        var broken = AnalysisFixtures.Valid() with
        {
            ImplementationGroups =
            [
                new AnalysisImplementationGroup
                {
                    AbstractionNodeId = AnalysisFixtures.ContractPath,
                    ImplementationNodeIds = [AnalysisFixtures.CallerPath],
                },
                new AnalysisImplementationGroup
                {
                    AbstractionNodeId = AnalysisFixtures.IconPath,
                    ImplementationNodeIds = [AnalysisFixtures.CallerPath],
                },
            ],
        };

        var error = AnalysisFixtures.Check(broken).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(AnalysisDiagnosticCodes.ImplementationGroupOverlap);
        error.Subject.ShouldBe(AnalysisFixtures.CallerPath);
        error.Message.ShouldContain(AnalysisFixtures.ContractPath);
        error.Message.ShouldContain(AnalysisFixtures.IconPath);
    }

    [Fact]
    public void An_abstraction_listed_as_its_own_implementation_is_refused()
    {
        var broken = AnalysisFixtures.Valid() with
        {
            ImplementationGroups =
            [
                new AnalysisImplementationGroup
                {
                    AbstractionNodeId = AnalysisFixtures.ContractPath,
                    ImplementationNodeIds = [AnalysisFixtures.CallerPath, AnalysisFixtures.ContractPath],
                },
            ],
        };

        var error = AnalysisFixtures.Check(broken).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(AnalysisDiagnosticCodes.ImplementationGroupOverlap);
        error.Message.ShouldContain("never its own implementation");
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

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.DanglingEdge);

        diagnostic.Message.ShouldContain("src/Ghost.cs");
        diagnostic.Message.ShouldContain("target");
        diagnostic.Subject.ShouldContain(AnalysisFixtures.ContractPath);

        // Edges belong to the whole result, not to a grouping, so the diagnostic names none.
        diagnostic.Grouping.ShouldBeNull();
    }

    [Fact]
    public void A_container_with_no_entry_node_is_named()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            DependencyContainers =
            [
                result.DependencyContainers[0] with { EntryNodeId = string.Empty },
                result.DependencyContainers[1],
            ],
        };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeFalse();

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.NoEntryNode);

        diagnostic.Subject.ShouldBe("contract-and-caller");
    }

    [Fact]
    public void An_entry_node_that_is_not_a_member_of_its_container_is_named()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            DependencyContainers =
            [
                result.DependencyContainers[0] with { EntryNodeId = AnalysisFixtures.IconPath },
                result.DependencyContainers[1],
            ],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.EntryNodeNotAMember &&
                d.Message.Contains(AnalysisFixtures.IconPath));
    }

    [Fact]
    public void An_entry_node_the_membership_list_does_not_start_with_is_named_along_with_the_one_it_does()
    {
        // The rule that replaced "rank 1 and the entry_point state". One declaration, one list, and
        // the two cannot disagree without saying so — which also means it can be stated per
        // grouping, where a single rank on a node could not.
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            DependencyContainers =
            [
                result.DependencyContainers[0] with
                {
                    EntryNodeId = AnalysisFixtures.CallerPath,
                    NodeIds = [AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath],
                },
                result.DependencyContainers[1],
            ],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.EntryNodeNotFirst &&
                d.Message.Contains(AnalysisFixtures.CallerPath) &&
                d.Message.Contains(AnalysisFixtures.ContractPath));
    }

    [Fact]
    public void A_container_that_lists_the_same_node_twice_is_named()
    {
        // Now that the list is the reading order, a repeat is not a harmless duplicate: it asks for
        // one node in two places in one walk.
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            DependencyContainers =
            [
                result.DependencyContainers[0] with
                {
                    NodeIds = [AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath, AnalysisFixtures.CallerPath],
                },
                result.DependencyContainers[1],
            ],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.DuplicateContainerMember &&
                d.Message.Contains(AnalysisFixtures.CallerPath));
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

        var diagnostic = validation.Errors.First(d => d.Code == AnalysisDiagnosticCodes.DuplicateNodeId);

        diagnostic.Subject.ShouldBe(AnalysisFixtures.ContractPath);
    }

    [Fact]
    public void Container_display_orders_that_repeat_are_reported()
    {
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            DependencyContainers =
            [
                result.DependencyContainers[0],
                result.DependencyContainers[1] with { DisplayOrder = 1 },
            ],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d =>
                d.Code == AnalysisDiagnosticCodes.DisplayOrderNotDense && d.Message.Contains("1, 1"));
    }

    [Fact]
    public void A_cycle_is_reported_as_a_warning_and_does_not_fail_the_run()
    {
        // The decision recorded in the plan: mutual dependencies are real, and rejecting them would
        // be asking the model to misdescribe the repository. Reading direction survives because it
        // comes from container order and membership order, not from following edges.
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

        var warning = validation.Warnings.First(d => d.Code == AnalysisDiagnosticCodes.Cycle);

        warning.Message.ShouldContain(AnalysisFixtures.ContractPath);
        warning.Message.ShouldContain(AnalysisFixtures.CallerPath);

        // Reported once, not once per grouping: a cycle is a fact about the edges, and the edges are
        // shared.
        validation.Warnings.Count(d => d.Code == AnalysisDiagnosticCodes.Cycle).ShouldBe(1);
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

        var diagnostic = validation.Warnings
            .Where(d => d.Code == AnalysisDiagnosticCodes.UnreachableNode)
            .ShouldHaveSingleItem();

        diagnostic.Subject.ShouldBe(AnalysisFixtures.CallerPath);
        diagnostic.Message.ShouldContain(AnalysisFixtures.ContractPath);
        diagnostic.Message.ShouldContain("contract-and-caller");

        // Only in dependency flow. The change-clusters grouping puts the caller in a cluster of its
        // own, where it is the whole walk and there is no gap to report — which is the same fact
        // seen from the other picture, and why this rule runs per grouping.
        diagnostic.Grouping.ShouldBe(AnalysisGrouping.DependencyFlow);
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
            DependencyContainers =
            [
                result.DependencyContainers[0] with
                {
                    EntryNodeId = "node-1",
                    NodeIds = ["node-1", AnalysisFixtures.CallerPath],
                },
                result.DependencyContainers[1],
            ],
            DependencyReadingOrder = ["node-1", AnalysisFixtures.CallerPath, AnalysisFixtures.IconPath],
            ClusterContainers =
            [
                AnalysisFixtures.Container("contracts", displayOrder: 1, nodeIds: ["node-1"]),
                result.ClusterContainers[1],
                result.ClusterContainers[2],
            ],
            ClusterReadingOrder = ["node-1", AnalysisFixtures.CallerPath, AnalysisFixtures.IconPath],
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
        var second = AnalysisFixtures.Node(
            AnalysisFixtures.CallerPath,
            id: AnalysisFixtures.CallerPath + "#logging");

        AnalysisFixtures.Check(With(AnalysisFixtures.Valid(), second)).IsValid.ShouldBeTrue();
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
            DependencyReadingOrder =
                [AnalysisFixtures.ContractPath, AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath],
        };

        AnalysisFixtures.Check(broken).Errors
            .ShouldContain(d => d.Code == AnalysisDiagnosticCodes.DuplicateReadingOrderEntry);
    }

    [Fact]
    public void An_incomplete_reading_order_is_a_warning_because_membership_order_already_answers_it()
    {
        var result = AnalysisFixtures.Valid();
        var broken = result with { DependencyReadingOrder = [AnalysisFixtures.ContractPath] };

        var validation = AnalysisFixtures.Check(broken);

        validation.IsValid.ShouldBeTrue();
        validation.Warnings.ShouldContain(d =>
            d.Code == AnalysisDiagnosticCodes.IncompleteReadingOrder &&
            d.Grouping == AnalysisGrouping.DependencyFlow);

        // And the traversal offered to the reviewer is the derived one, covering everything.
        AnalysisReadingOrder.Resolve(broken, AnalysisGrouping.DependencyFlow).ShouldBe(
            [AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath, AnalysisFixtures.IconPath]);
    }

    [Fact]
    public void Each_grouping_gets_the_reading_order_it_stated()
    {
        // Requirement 5: reading order is computed per mode. The fixture's two groupings order the
        // same three nodes differently once derived, because their containers differ.
        var result = AnalysisFixtures.Valid() with
        {
            DependencyReadingOrder = [],
            ClusterReadingOrder = [],
            ClusterContainers =
            [
                AnalysisFixtures.Container("assets", displayOrder: 1, nodeIds: [AnalysisFixtures.IconPath]),
                AnalysisFixtures.Container("contracts", displayOrder: 2, nodeIds: [AnalysisFixtures.ContractPath]),
                AnalysisFixtures.Container("call-sites", displayOrder: 3, nodeIds: [AnalysisFixtures.CallerPath]),
            ],
        };

        AnalysisReadingOrder.Resolve(result, AnalysisGrouping.DependencyFlow).ShouldBe(
            [AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath, AnalysisFixtures.IconPath]);

        AnalysisReadingOrder.Resolve(result, AnalysisGrouping.ChangeClusters).ShouldBe(
            [AnalysisFixtures.IconPath, AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath]);
    }

    [Fact]
    public void Every_error_message_names_something_a_model_can_act_on()
    {
        // A blanket check over the whole taxonomy: whatever else a diagnostic says, it quotes its
        // subject, so "fix this" is never advice without an address.
        var result = AnalysisFixtures.Valid();

        var broken = result with
        {
            Nodes = [result.Nodes[0] with { Importance = 0 }, result.Nodes[1] with { Title = " " }, result.Nodes[2]],
            Edges = [result.Edges[0] with { SourceNodeId = "nowhere" }],
            DependencyContainers =
            [
                result.DependencyContainers[0] with { EntryNodeId = "nowhere" },
                result.DependencyContainers[1] with { DisplayOrder = 4 },
            ],
        };

        var errors = AnalysisFixtures.Check(broken).Errors;

        errors.ShouldNotBeEmpty();
        errors.ShouldAllBe(d => d.Message.Length > 20);
        errors.Where(d => d.Subject.Length > 0).ShouldAllBe(d => d.Message.Contains(d.Subject));
    }

    [Fact]
    public void An_empty_changeset_and_an_empty_result_agree_with_each_other()
    {
        // Not reachable in production — a clean changeset fails the run before a token is spent — but
        // the two halves of "nothing to say" should agree rather than argue.
        var empty = new AnalysisResult { Summary = "Nothing changed." };

        AnalysisValidator
            .Validate(empty, [], AnalysisRunOptions.Default with { ChangeClusters = false, ImplementationGroups = false })
            .IsValid.ShouldBeTrue();
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

        // Once, not once per grouping: the prose on a node is shared by both.
        validation.Warnings
            .Where(d => d.Code == AnalysisDiagnosticCodes.VerboseField)
            .ShouldHaveSingleItem()
            .Subject.ShouldBe(result.Nodes[0].Id);
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
        var budgets = AnalysisFieldBudgets.For(AnalysisRunOptions.Default.Verbosity);
        var result = AnalysisFixtures.Valid();
        var verbose = result with
        {
            Summary = new string('x', budgets.OverallSummary * 3),
            OverallRisks = [new string('x', budgets.Risk * 3)],
            DependencyContainers =
            [
                result.DependencyContainers[0] with
                {
                    Explanation = new string('x', budgets.ContainerExplanation * 3),
                },
                result.DependencyContainers[1],
            ],
        };

        var warnings = AnalysisFixtures.Check(verbose).Warnings
            .Where(d => d.Code == AnalysisDiagnosticCodes.VerboseField)
            .ToList();

        warnings.Count.ShouldBe(3);

        // The two about the change as a whole carry no subject; the container one names itself, so
        // a reader knows what to shorten, and which grouping to shorten it in.
        warnings.Count(d => d.Subject.Length == 0).ShouldBe(2);
        warnings.ShouldContain(d =>
            d.Subject == result.DependencyContainers[0].Id &&
            d.Grouping == AnalysisGrouping.DependencyFlow);
    }

    [Fact]
    public void A_result_describing_a_changeset_it_was_not_given_fails_on_every_file()
    {
        var result = AnalysisFixtures.Valid();
        var other = new[] { AnalysisFixtures.File("src/Elsewhere.cs") };

        var validation = AnalysisValidator.Validate(
            result,
            other,
            AnalysisRunOptions.Default with { ImplementationGroups = false });

        validation.IsValid.ShouldBeFalse();
        validation.Errors.Count(static d => d.Code == AnalysisDiagnosticCodes.UnknownFile).ShouldBe(3);
        validation.Errors.ShouldContain(d =>
            d.Code == AnalysisDiagnosticCodes.FileNotCovered && d.Subject == "src/Elsewhere.cs");
    }

    [Fact]
    public void A_run_without_node_explanations_is_not_asked_for_the_prose_it_was_never_given_room_for()
    {
        // The schema that run answered in had no whatChanged or whyItChanged, so an empty one is the
        // only answer the model could give — rejecting it would be a repair round nobody can pass.
        var result = AnalysisFixtures.Valid();
        var titlesOnly = result with
        {
            Nodes = [.. result.Nodes.Select(static node => node with { WhatChanged = string.Empty, WhyItChanged = string.Empty })],
        };

        AnalysisFixtures
            .Check(titlesOnly, AnalysisRunOptions.Default with { NodeExplanations = false })
            .IsValid.ShouldBeTrue();

        // The same answer to a run that did ask for them is still wrong.
        AnalysisFixtures.Check(titlesOnly).Errors
            .ShouldContain(d => d.Code == AnalysisDiagnosticCodes.EmptyNodeField);
    }

    [Fact]
    public void A_run_without_node_explanations_still_insists_on_a_title()
    {
        var result = AnalysisFixtures.Valid();
        var untitled = result with
        {
            Nodes = [result.Nodes[0] with { Title = " " }, result.Nodes[1], result.Nodes[2]],
        };

        AnalysisFixtures.Check(untitled, AnalysisRunOptions.Default with { NodeExplanations = false }).Errors
            .ShouldHaveSingleItem().Code.ShouldBe(AnalysisDiagnosticCodes.EmptyNodeField);
    }

    [Theory]
    [InlineData(AnalysisVerbosity.Brief)]
    [InlineData(AnalysisVerbosity.Medium)]
    [InlineData(AnalysisVerbosity.Detailed)]
    public void The_length_warning_is_measured_against_the_verbosity_the_run_asked_for(AnalysisVerbosity verbosity)
    {
        // The prompt asked for this verbosity's lengths, so "too long" means too long for those: a
        // detailed run is not reported for writing what it was asked to write.
        var budget = AnalysisFieldBudgets.For(verbosity).NodeProse;
        var options = AnalysisRunOptions.Default with { Verbosity = verbosity };
        var result = AnalysisFixtures.Valid();

        AnalysisResult WithWhatChanged(int length) => result with
        {
            Nodes = [result.Nodes[0] with { WhatChanged = new string('x', length) }, result.Nodes[1], result.Nodes[2]],
        };

        AnalysisFixtures.Check(WithWhatChanged(budget * AnalysisFieldBudgets.OvershootFactor), options).Warnings
            .ShouldNotContain(d => d.Code == AnalysisDiagnosticCodes.VerboseField);

        AnalysisFixtures.Check(WithWhatChanged((budget * AnalysisFieldBudgets.OvershootFactor) + 1), options).Warnings
            .ShouldContain(d => d.Code == AnalysisDiagnosticCodes.VerboseField && d.Subject == result.Nodes[0].Id);
    }

    [Fact]
    public void The_three_verbosities_ask_for_rising_lengths_and_brief_is_the_default()
    {
        var brief = AnalysisFieldBudgets.For(AnalysisVerbosity.Brief);
        var medium = AnalysisFieldBudgets.For(AnalysisVerbosity.Medium);
        var detailed = AnalysisFieldBudgets.For(AnalysisVerbosity.Detailed);

        foreach (var pick in new Func<AnalysisFieldBudgetSet, int>[]
        {
            static b => b.NodeProse,
            static b => b.ContainerSummary,
            static b => b.ContainerExplanation,
            static b => b.OverallSummary,
            static b => b.Risk,
        })
        {
            pick(brief).ShouldBeLessThan(pick(medium));
            pick(medium).ShouldBeLessThan(pick(detailed));
        }

        // Titles are labels the renderer clamps, so they do not grow.
        detailed.NodeTitle.ShouldBe(brief.NodeTitle);

        AnalysisRunOptions.Default.Verbosity.ShouldBe(AnalysisVerbosity.Brief);
    }

    /// <summary>
    /// Drops a node from the result and from both groupings, so a test about the shared node set is
    /// not also a test about a grouping that now has a hole in it.
    /// </summary>
    private static AnalysisResult Without(AnalysisResult result, string path) => result with
    {
        Nodes = [.. result.Nodes.Where(node => node.FilePath != path)],
        DependencyContainers = [.. Purge(result.DependencyContainers, path)],
        DependencyReadingOrder = [.. result.DependencyReadingOrder.Where(id => id != path)],
        ClusterContainers = [.. Purge(result.ClusterContainers, path)],
        ClusterReadingOrder = [.. result.ClusterReadingOrder.Where(id => id != path)],
    };

    /// <inheritdoc cref="Without"/>
    private static AnalysisResult With(AnalysisResult result, AnalysisNode node) => result with
    {
        Nodes = [.. result.Nodes, node],
        DependencyContainers = [.. Extend(result.DependencyContainers, node.Id)],
        DependencyReadingOrder = [.. result.DependencyReadingOrder, node.Id],
        ClusterContainers = [.. Extend(result.ClusterContainers, node.Id)],
        ClusterReadingOrder = [.. result.ClusterReadingOrder, node.Id],
    };

    private static IEnumerable<AnalysisContainer> Purge(
        IReadOnlyList<AnalysisContainer> containers,
        string nodeId)
    {
        var order = 1;

        foreach (var container in containers)
        {
            var members = container.NodeIds.Where(id => id != nodeId).ToArray();

            if (members.Length == 0)
            {
                continue;
            }

            yield return container with { NodeIds = members, DisplayOrder = order++ };
        }
    }

    private static IEnumerable<AnalysisContainer> Extend(
        IReadOnlyList<AnalysisContainer> containers,
        string nodeId)
    {
        for (var index = 0; index < containers.Count; index++)
        {
            yield return index == 0
                ? containers[index] with { NodeIds = [.. containers[index].NodeIds, nodeId] }
                : containers[index];
        }
    }
}
