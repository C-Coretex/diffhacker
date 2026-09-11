using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Tests;

/// <summary>
/// A small, valid analysis and the changeset it describes, plus the pieces to build variants of it.
/// <para>
/// Every validator test starts from something that passes and breaks exactly one thing. That is the
/// only way an assertion means what it claims: a test that starts from a malformed document and
/// asserts "it failed" would pass for the wrong reason forever.
/// </para>
/// </summary>
internal static class AnalysisFixtures
{
    public const string ContractPath = "src/Contract.cs";
    public const string CallerPath = "src/Caller.cs";
    public const string IconPath = "assets/icon.png";

    /// <summary>Three files: one modified, one modified, one deleted binary.</summary>
    public static IReadOnlyList<ChangedFile> Changeset() =>
    [
        File(ContractPath, ChangeStatus.Modified, linesAdded: 12, linesRemoved: 3),
        File(CallerPath, ChangeStatus.Modified, linesAdded: 4, linesRemoved: 4),
        File(IconPath, ChangeStatus.Deleted, binary: true),
    ];

    public static ChangedFile File(
        string path,
        ChangeStatus status = ChangeStatus.Modified,
        int? linesAdded = 1,
        int? linesRemoved = 1,
        bool binary = false) => new()
        {
            Path = path,
            Status = status,
            LinesAdded = binary ? null : linesAdded,
            LinesRemoved = binary ? null : linesRemoved,
            IsBinary = binary,
            Language = binary ? null : "C#",
            Project = new ProjectReference("DiffHacker", "src", "DiffHacker.csproj"),
        };

    /// <summary>
    /// A result that passes every rule, for a test to break one thing in.
    /// <para>
    /// It holds both groupings, because that is what a run produces: two clusters that keep the
    /// contract-to-caller path whole, and three thematic ones that split it. A fixture with one
    /// grouping would let every per-grouping rule pass having been checked once.
    /// </para>
    /// </summary>
    public static AnalysisResult Valid() => new()
    {
        Summary = "The contract grew a field and its one caller was updated to pass it.",
        OverallRisks = ["The icon was removed without a replacement being added."],
        DependencyReadingOrder = [ContractPath, CallerPath, IconPath],
        DependencyContainers =
        [
            Container(
                "contract-and-caller",
                displayOrder: 1,
                nodeIds: [ContractPath, CallerPath],
                summary: "A field was added and the call site follows it."),
            Container(
                "removed-assets",
                displayOrder: 2,
                nodeIds: [IconPath],
                summary: "An icon nothing references any more."),
        ],
        ClusterReadingOrder = [ContractPath, CallerPath, IconPath],
        ClusterContainers =
        [
            Container("contracts", displayOrder: 1, nodeIds: [ContractPath]),
            Container("call-sites", displayOrder: 2, nodeIds: [CallerPath]),
            Container("assets", displayOrder: 3, nodeIds: [IconPath]),
        ],
        Nodes =
        [
            Node(ContractPath, importance: 5),
            Node(CallerPath, importance: 2),
            Node(IconPath, importance: 1, states: [AnalysisNodeState.Deleted]),
        ],
        Edges =
        [
            new AnalysisEdge
            {
                SourceNodeId = ContractPath,
                TargetNodeId = CallerPath,
                Kind = AnalysisEdgeKind.Direct,
                Explanation = "The caller constructs the contract and had to pass the new field.",
                Risks = [],
            },
        ],

        // Asked for, and this change has none — the answer most runs give.
        ImplementationGroups = [],
    };

    /// <summary>The same result from a run that was not asked for the second grouping.</summary>
    public static AnalysisResult DependencyOnly() =>
        Valid() with { ClusterContainers = [], ClusterReadingOrder = [] };

    /// <summary>A container whose entry node is the first of its members, as validation requires.</summary>
    public static AnalysisContainer Container(
        string id,
        int displayOrder,
        IReadOnlyList<string> nodeIds,
        string? summary = null,
        string? entryNodeId = null) => new()
        {
            Id = id,
            Title = $"The {id} cluster",
            Summary = summary ?? $"What {id} is about.",
            Explanation = $"The longer account of {id}.",
            Risks = [],
            DisplayOrder = displayOrder,
            EntryNodeId = entryNodeId ?? nodeIds[0],
            NodeIds = nodeIds,
        };

    public static AnalysisNode Node(
        string path,
        int importance = 3,
        IReadOnlyList<AnalysisNodeState>? states = null,
        string? id = null) => new()
        {
            Id = id ?? path,
            FilePath = path,
            Title = "What this file does in the change",
            WhatChanged = "A field was added.",
            WhyItChanged = "The caller needed to pass a tenant.",
            HowItAffectsOthers = string.Empty,
            ImplementationNotes = string.Empty,
            Risks = [],
            Importance = importance,
            States = states ?? [AnalysisNodeState.Changed],
        };

    /// <summary>The messages of every error, for an assertion that names what it expects.</summary>
    public static IReadOnlyList<string> ErrorsOf(AnalysisResult result) =>
        Check(result).ErrorMessages;

    public static AnalysisValidation Check(
        AnalysisResult result,
        bool expectChangeClusters = true,
        bool expectImplementationGroups = true) =>
        AnalysisValidator.Validate(result, Changeset(), expectChangeClusters, expectImplementationGroups);

    /// <summary>
    /// <see cref="Valid"/> with the contract declared as an abstraction its caller implements. The two
    /// share a cluster in dependency flow and are split in change clusters, so it passes with one
    /// warning about the second grouping and none about the first.
    /// </summary>
    public static AnalysisResult WithImplementationGroup() => Valid() with
    {
        ImplementationGroups =
        [
            new AnalysisImplementationGroup
            {
                AbstractionNodeId = ContractPath,
                ImplementationNodeIds = [CallerPath],
            },
        ],
    };
}
