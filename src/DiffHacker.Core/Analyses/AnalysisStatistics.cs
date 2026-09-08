using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// Numbers computed beside the model's answer rather than asked of it.
/// <para>
/// Requirement 8 wants these <i>alongside</i> the LLM output, and the distinction is the point: a
/// model asked how many files it covered will answer, plausibly, and be wrong. Everything here is
/// counted from the changeset and from the graph the model returned, so the figures cannot flatter
/// the result they describe.
/// </para>
/// </summary>
public sealed record AnalysisStatistics
{
    public required ChangesetStatistics Changeset { get; init; }

    public required int ContainerCount { get; init; }

    /// <summary>Nodes in the graph. Above the file count when a file was split.</summary>
    public required int NodeCount { get; init; }

    public required int EdgeCount { get; init; }

    public required int DirectEdgeCount { get; init; }

    public required int ConceptualEdgeCount { get; init; }

    public required int LargestContainerSize { get; init; }

    public required int SmallestContainerSize { get; init; }

    /// <summary>Nodes marked risky or carrying at least one risk of their own.</summary>
    public required int RiskyNodeCount { get; init; }

    /// <summary>Every risk anywhere in the result: node, edge, container and overall together.</summary>
    public required int RiskCount { get; init; }

    /// <inheritdoc cref="AnalysisGraph.LongestChain"/>
    public required int LongestChain { get; init; }

    public required int HighestFanIn { get; init; }

    public required int HighestFanOut { get; init; }

    public string? HighestFanInNodeId { get; init; }

    public string? HighestFanOutNodeId { get; init; }

    public static AnalysisStatistics From(
        AnalysisResult result,
        ChangesetStatistics changeset,
        AnalysisGraph graph)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(changeset);
        ArgumentNullException.ThrowIfNull(graph);

        var containerSizes = result.Containers.Select(static c => c.NodeIds.Count).ToArray();

        return new AnalysisStatistics
        {
            Changeset = changeset,
            ContainerCount = result.Containers.Count,
            NodeCount = result.Nodes.Count,
            EdgeCount = result.Edges.Count,
            DirectEdgeCount = result.Edges.Count(static e => e.Kind is AnalysisEdgeKind.Direct),
            ConceptualEdgeCount = result.Edges.Count(static e => e.Kind is AnalysisEdgeKind.Conceptual),
            LargestContainerSize = containerSizes.Length == 0 ? 0 : containerSizes.Max(),
            SmallestContainerSize = containerSizes.Length == 0 ? 0 : containerSizes.Min(),

            // Either signal counts. A node the model marked risky without listing a risk is still
            // risky, and one that listed a risk without marking itself is too.
            RiskyNodeCount = result.Nodes.Count(static node =>
                node.Risks.Count > 0 || node.States.Contains(AnalysisNodeState.Risky)),

            RiskCount = result.OverallRisks.Count
                + result.Containers.Sum(static c => c.Risks.Count)
                + result.Nodes.Sum(static n => n.Risks.Count)
                + result.Edges.Sum(static e => e.Risks.Count),

            LongestChain = graph.LongestChain,
            HighestFanIn = graph.HighestFanIn,
            HighestFanOut = graph.HighestFanOut,
            HighestFanInNodeId = graph.HighestFanInNodeId,
            HighestFanOutNodeId = graph.HighestFanOutNodeId,
        };
    }
}
