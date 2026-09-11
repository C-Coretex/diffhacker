using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using DiffHacker.Contracts;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// The model's whole description of one changeset, as <c>schema/analysis-result.schema.json</c>
/// defines it.
/// <para>
/// Hand-written here rather than taken from the generated contract, for the same reason
/// <see cref="Knowledge.ProjectProfileDocument"/> is: the generated record is a class with a
/// fourteen-argument constructor in alphabetical order, and a validator and its tests that
/// construct nodes positionally would silently mis-assign two <c>string</c> arguments the day a
/// schema field is renamed. The properties are named to match the schema exactly, so
/// <c>JsonSerializerDefaults.Web</c> maps one onto the other with no mapping layer to drift.
/// </para>
/// </summary>
public sealed record AnalysisResult
{
    /// <summary>What this change does as a whole. Carries no risks — those have their own field.</summary>
    public required string Summary { get; init; }

    /// <summary>Risks belonging to the change as a whole.</summary>
    public IReadOnlyList<string> OverallRisks { get; init; } = [];

    /// <summary>
    /// The dependency-flow grouping: clusters that keep a complete change path intact.
    /// <para>
    /// The two groupings are four flat properties rather than a list of grouping objects because a
    /// schema definition may not reference another one, and a grouping needs the container
    /// definition. Ask for one through <see cref="For"/> rather than reading these directly.
    /// </para>
    /// </summary>
    public IReadOnlyList<AnalysisContainer> DependencyContainers { get; init; } = [];

    /// <inheritdoc cref="DependencyContainers"/>
    public IReadOnlyList<string> DependencyReadingOrder { get; init; } = [];

    /// <summary>
    /// The change-clusters grouping: the same nodes regrouped by theme. Empty when the run was not
    /// asked for it, and on an analysis written before groupings existed.
    /// </summary>
    public IReadOnlyList<AnalysisContainer> ClusterContainers { get; init; } = [];

    /// <inheritdoc cref="ClusterContainers"/>
    public IReadOnlyList<string> ClusterReadingOrder { get; init; } = [];

    public IReadOnlyList<AnalysisNode> Nodes { get; init; } = [];

    public IReadOnlyList<AnalysisEdge> Edges { get; init; } = [];

    /// <summary>
    /// Abstractions changed alongside their implementations, which the renderer may draw as one box.
    /// <para>
    /// Null and empty are different answers, and that is why this is nullable where every other list
    /// here defaults to empty. <b>Null</b> means nobody asked: a run told not to produce them, or a
    /// document written before 1.13, which has no such field and is read as it stands. <b>Empty</b>
    /// means the model was asked and this change has none. A toolbar control needs to tell a reviewer
    /// which of those it is, and the document is the only thing that knows.
    /// </para>
    /// </summary>
    public IReadOnlyList<AnalysisImplementationGroup>? ImplementationGroups { get; init; }

    /// <summary>
    /// The groupings this result actually holds. Dependency flow always; change clusters only when
    /// the run produced them, which is what lets a control offering the other one be disabled rather
    /// than left to fail.
    /// </summary>
    public IReadOnlyList<AnalysisGrouping> Groupings => ClusterContainers.Count > 0
        ? [AnalysisGrouping.DependencyFlow, AnalysisGrouping.ChangeClusters]
        : [AnalysisGrouping.DependencyFlow];

    /// <summary>One grouping, named. A grouping the result does not hold reads as empty, never throws.</summary>
    public AnalysisGroupingView For(AnalysisGrouping grouping) => grouping switch
    {
        AnalysisGrouping.DependencyFlow => new AnalysisGroupingView
        {
            Grouping = grouping,
            Containers = DependencyContainers,
            ReadingOrder = DependencyReadingOrder,
        },
        AnalysisGrouping.ChangeClusters => new AnalysisGroupingView
        {
            Grouping = grouping,
            Containers = ClusterContainers,
            ReadingOrder = ClusterReadingOrder,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "Unknown grouping."),
    };
}

/// <summary>One cluster of interconnected change, within one grouping.</summary>
public sealed record AnalysisContainer
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string Explanation { get; init; }

    public IReadOnlyList<string> Risks { get; init; } = [];

    /// <summary>Where this container sits among the containers of its grouping, from 1.</summary>
    public required int DisplayOrder { get; init; }

    /// <summary>The node a reviewer starts from: the first of <see cref="NodeIds"/>. One per container.</summary>
    public required string EntryNodeId { get; init; }

    /// <summary>
    /// Ids of the nodes in this container, in the order they are read. Membership is declared here
    /// rather than on the node so that "in two containers" and "in none" are both things the
    /// validator can see and name — and, since Iteration 11, because a node belongs to one container
    /// per grouping and could not carry a single answer.
    /// <para>
    /// The <i>order</i> is here for the same reason. Rank used to be an integer on the node; two
    /// groupings cannot both be described by one integer, and expressing the order as the order of
    /// this list also removed a whole class of repair round — a dense 1..n across three hundred
    /// nodes was something models got wrong regularly.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> NodeIds { get; init; } = [];
}

/// <summary>A file, or a specific place inside a file, participating in the change.</summary>
public sealed record AnalysisNode
{
    /// <summary>
    /// Path-derived and therefore stable across runs: the file path, or the file path, a '#' and a
    /// slug when one file yields several nodes. <see cref="AnalysisNodeId"/> is what checks it.
    /// </summary>
    public required string Id { get; init; }

    public required string FilePath { get; init; }

    /// <summary>The symbol this node is about, or empty when the node is the whole file.</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>First line of the region, or zero when the node is not a line range.</summary>
    public int StartLine { get; init; }

    /// <inheritdoc cref="StartLine"/>
    public int EndLine { get; init; }

    public required string Title { get; init; }

    public required string WhatChanged { get; init; }

    public required string WhyItChanged { get; init; }

    public string HowItAffectsOthers { get; init; } = string.Empty;

    public string ImplementationNotes { get; init; } = string.Empty;

    /// <summary>
    /// What could go wrong because of this node. Its own field on purpose: §0.2 keeps risks out of
    /// the explanations so the reviewer can read them as a column.
    /// </summary>
    public IReadOnlyList<string> Risks { get; init; } = [];

    /// <summary>How much this node matters, 1 to 5.</summary>
    public required int Importance { get; init; }

    /// <summary>
    /// What is true of this node, in every grouping. Where it starts a cluster is not here: that
    /// belongs to a container of one grouping, and <see cref="AnalysisContainer.EntryNodeId"/> says
    /// it. The wire adds an entry_point state for the active grouping.
    /// </summary>
    public IReadOnlyList<AnalysisNodeState> States { get; init; } = [];
}

/// <summary>A relationship between two nodes, expressing reading flow.</summary>
public sealed record AnalysisEdge
{
    public required string SourceNodeId { get; init; }

    public required string TargetNodeId { get; init; }

    public required AnalysisEdgeKind Kind { get; init; }

    public required string Explanation { get; init; }

    public IReadOnlyList<string> Risks { get; init; } = [];
}

/// <summary>
/// One abstraction — an interface, an abstract base, a trait, a protocol, a header — and the nodes
/// implementing it. Declared by the model, because the application reads no language and so cannot
/// tell an interface from anything else (§0.2.3).
/// </summary>
public sealed record AnalysisImplementationGroup
{
    public required string AbstractionNodeId { get; init; }

    /// <summary>The nodes implementing it, in reading order. At least one, never the abstraction.</summary>
    public IReadOnlyList<string> ImplementationNodeIds { get; init; } = [];
}

/// <summary>What is true of a node. Not mutually exclusive.</summary>
[JsonConverter(typeof(SchemaEnumConverter<AnalysisNodeState>))]
public enum AnalysisNodeState
{
    [EnumMember(Value = "changed")]
    Changed,

    [EnumMember(Value = "added")]
    Added,

    [EnumMember(Value = "deleted")]
    Deleted,

    /// <summary>Did not change, but has to be read to follow the change.</summary>
    [EnumMember(Value = "unchanged_relevant")]
    UnchangedRelevant,

    [EnumMember(Value = "risky")]
    Risky,
}

/// <summary>Whether an edge is backed by code or inferred.</summary>
[JsonConverter(typeof(SchemaEnumConverter<AnalysisEdgeKind>))]
public enum AnalysisEdgeKind
{
    /// <summary>Backed by an actual code-level dependency.</summary>
    [EnumMember(Value = "direct")]
    Direct,

    /// <summary>Inferred from intent, workflow or reading order. §0.2.6 allows it explicitly.</summary>
    [EnumMember(Value = "conceptual")]
    Conceptual,
}
