using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Tests;

/// <summary>
/// The walked parts of requirement 8 — fan-in, fan-out, cycles and the longest chain.
/// <para>
/// The longest chain is the one worth watching. Measured over the raw graph it would be unbounded
/// the moment the model reports a mutual dependency, and the decision to accept cycles rather than
/// reject them makes that a case the code will actually meet.
/// </para>
/// </summary>
public sealed class AnalysisGraphTests
{
    [Fact]
    public void An_empty_graph_measures_zero_rather_than_throwing()
    {
        var graph = AnalysisGraph.Build([], []);

        graph.LongestChain.ShouldBe(0);
        graph.HighestFanIn.ShouldBe(0);
        graph.HighestFanOut.ShouldBe(0);
        graph.HighestFanInNodeId.ShouldBeNull();
        graph.Cycles.ShouldBeEmpty();
    }

    [Fact]
    public void A_lone_node_is_a_chain_of_one()
    {
        AnalysisGraph.Build([Node("a")], []).LongestChain.ShouldBe(1);
    }

    [Fact]
    public void A_straight_line_is_measured_in_nodes_not_edges()
    {
        var graph = AnalysisGraph.Build(
            [Node("a"), Node("b"), Node("c")],
            [Edge("a", "b"), Edge("b", "c")]);

        graph.LongestChain.ShouldBe(3);
        graph.Cycles.ShouldBeEmpty();
    }

    [Fact]
    public void The_longest_of_several_branches_is_the_one_reported()
    {
        var graph = AnalysisGraph.Build(
            [Node("a"), Node("b"), Node("c"), Node("d"), Node("e")],
            [Edge("a", "b"), Edge("b", "c"), Edge("c", "d"), Edge("a", "e")]);

        graph.LongestChain.ShouldBe(4);
    }

    [Fact]
    public void A_cycle_is_counted_as_the_one_component_it_is_rather_than_looping_forever()
    {
        // a → b ⇄ c → d. The cycle contributes its two nodes once, so the chain is four.
        var graph = AnalysisGraph.Build(
            [Node("a"), Node("b"), Node("c"), Node("d")],
            [Edge("a", "b"), Edge("b", "c"), Edge("c", "b"), Edge("c", "d")]);

        graph.LongestChain.ShouldBe(4);
        graph.Cycles.ShouldHaveSingleItem().ShouldBe(["b", "c"]);
    }

    [Fact]
    public void Two_separate_cycles_are_reported_separately_and_in_a_stable_order()
    {
        var graph = AnalysisGraph.Build(
            [Node("a"), Node("b"), Node("x"), Node("y")],
            [Edge("a", "b"), Edge("b", "a"), Edge("x", "y"), Edge("y", "x")]);

        graph.Cycles.Count.ShouldBe(2);
        graph.Cycles[0].ShouldBe(["a", "b"]);
        graph.Cycles[1].ShouldBe(["x", "y"]);
    }

    [Fact]
    public void A_node_pointing_at_itself_is_not_reported_as_a_cycle_of_one()
    {
        // The validator reports it on its own, as a self-edge. Two diagnostics for one mistake
        // would be two things for the reader to work out are the same thing.
        var graph = AnalysisGraph.Build([Node("a")], [Edge("a", "a")]);

        graph.Cycles.ShouldBeEmpty();
        graph.HighestFanOut.ShouldBe(0);
    }

    [Fact]
    public void Fan_in_and_fan_out_name_the_busiest_node()
    {
        var graph = AnalysisGraph.Build(
            [Node("hub"), Node("a"), Node("b"), Node("c")],
            [Edge("a", "hub"), Edge("b", "hub"), Edge("c", "hub"), Edge("hub", "a")]);

        graph.HighestFanIn.ShouldBe(3);
        graph.HighestFanInNodeId.ShouldBe("hub");
        graph.HighestFanOut.ShouldBe(1);
    }

    [Fact]
    public void A_tie_on_fan_in_is_broken_by_id_so_two_runs_name_the_same_node()
    {
        var graph = AnalysisGraph.Build(
            [Node("zebra"), Node("alpha"), Node("source")],
            [Edge("source", "zebra"), Edge("source", "alpha")]);

        graph.HighestFanIn.ShouldBe(1);
        graph.HighestFanInNodeId.ShouldBe("alpha");
    }

    [Fact]
    public void An_edge_with_an_end_that_does_not_exist_is_dropped_rather_than_crashing_the_walk()
    {
        // Validation reports it by name; the walk simply must not fall over on the way there.
        var graph = AnalysisGraph.Build([Node("a")], [Edge("a", "ghost"), Edge("ghost", "a")]);

        graph.LongestChain.ShouldBe(1);
        graph.HighestFanIn.ShouldBe(0);
    }

    [Fact]
    public void A_deep_chain_is_walked_without_the_call_stack()
    {
        // §0.2.10 allows fifteen hundred files, and a graph that deep would overflow a recursive
        // Tarjan. This is the test that would fail if the explicit stack were ever "simplified".
        var nodes = Enumerable.Range(0, 5_000).Select(i => Node($"n{i}")).ToArray();
        var edges = Enumerable.Range(0, 4_999).Select(i => Edge($"n{i}", $"n{i + 1}")).ToArray();

        AnalysisGraph.Build(nodes, edges).LongestChain.ShouldBe(5_000);
    }

    [Fact]
    public void Statistics_count_risks_everywhere_they_are_recorded()
    {
        var result = AnalysisFixtures.Valid();

        var withRisks = result with
        {
            Nodes = [result.Nodes[0] with { Risks = ["A risk on a node."] }, result.Nodes[1], result.Nodes[2]],
            Edges = [result.Edges[0] with { Risks = ["A risk on an edge."] }],
            DependencyContainers =
            [
                result.DependencyContainers[0] with { Risks = ["A risk on a container."] },
                result.DependencyContainers[1],
            ],
        };

        var statistics = AnalysisStatistics.From(
            withRisks,
            AnalysisGrouping.DependencyFlow,
            ChangesetStatistics.From(AnalysisFixtures.Changeset()),
            AnalysisGraph.Build(withRisks.Nodes, withRisks.Edges));

        // One overall, one container, one node, one edge.
        statistics.RiskCount.ShouldBe(4);
        statistics.RiskyNodeCount.ShouldBe(1);
    }

    [Fact]
    public void A_node_marked_risky_without_listing_one_still_counts_as_risky()
    {
        var result = AnalysisFixtures.Valid();

        var marked = result with
        {
            Nodes =
            [
                result.Nodes[0] with { States = [AnalysisNodeState.Changed, AnalysisNodeState.Risky] },
                result.Nodes[1],
                result.Nodes[2],
            ],
        };

        AnalysisStatistics.From(
            marked,
            AnalysisGrouping.DependencyFlow,
            ChangesetStatistics.From(AnalysisFixtures.Changeset()),
            AnalysisGraph.Build(marked.Nodes, marked.Edges)).RiskyNodeCount.ShouldBe(1);
    }

    private static AnalysisNode Node(string id) => AnalysisFixtures.Node(id);

    private static AnalysisEdge Edge(string source, string target) => new()
    {
        SourceNodeId = source,
        TargetNodeId = target,
        Kind = AnalysisEdgeKind.Direct,
        Explanation = "Read one after the other.",
    };
}
