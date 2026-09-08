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

    /// <summary>Node ids in the order a reviewer should walk the whole change.</summary>
    public IReadOnlyList<string> ReadingOrder { get; init; } = [];

    public IReadOnlyList<AnalysisContainer> Containers { get; init; } = [];

    public IReadOnlyList<AnalysisNode> Nodes { get; init; } = [];

    public IReadOnlyList<AnalysisEdge> Edges { get; init; } = [];
}

/// <summary>One cluster of interconnected change.</summary>
public sealed record AnalysisContainer
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string Explanation { get; init; }

    public IReadOnlyList<string> Risks { get; init; } = [];

    /// <summary>Where this container sits among the containers, from 1.</summary>
    public required int DisplayOrder { get; init; }

    /// <summary>The node a reviewer starts from. Exactly one per container.</summary>
    public required string EntryNodeId { get; init; }

    /// <summary>
    /// Ids of the nodes in this container. Membership is declared here rather than on the node so
    /// that "in two containers" and "in none" are both things the validator can see and name.
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

    /// <summary>Position within its own container, from 1. Rank 1 is the entry node.</summary>
    public required int Rank { get; init; }

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

    [EnumMember(Value = "entry_point")]
    EntryPoint,
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
