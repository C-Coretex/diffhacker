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

    /// <summary>A result that passes every rule, for a test to break one thing in.</summary>
    public static AnalysisResult Valid() => new()
    {
        Summary = "The contract grew a field and its one caller was updated to pass it.",
        OverallRisks = ["The icon was removed without a replacement being added."],
        ReadingOrder = [ContractPath, CallerPath, IconPath],
        Containers =
        [
            new AnalysisContainer
            {
                Id = "contract-and-caller",
                Title = "The contract and its caller",
                Summary = "A field was added and the call site follows it.",
                Explanation = "The contract is the decision; the caller is the consequence.",
                Risks = [],
                DisplayOrder = 1,
                EntryNodeId = ContractPath,
                NodeIds = [ContractPath, CallerPath],
            },
            new AnalysisContainer
            {
                Id = "removed-assets",
                Title = "Removed assets",
                Summary = "An icon nothing references any more.",
                Explanation = "Unrelated to the contract change; grouped separately for that reason.",
                Risks = [],
                DisplayOrder = 2,
                EntryNodeId = IconPath,
                NodeIds = [IconPath],
            },
        ],
        Nodes =
        [
            Node(ContractPath, rank: 1, importance: 5, states: [AnalysisNodeState.Changed, AnalysisNodeState.EntryPoint]),
            Node(CallerPath, rank: 2, importance: 2, states: [AnalysisNodeState.Changed]),
            Node(IconPath, rank: 1, importance: 1, states: [AnalysisNodeState.Deleted, AnalysisNodeState.EntryPoint]),
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
    };

    public static AnalysisNode Node(
        string path,
        int rank,
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
            Rank = rank,
            States = states ?? [AnalysisNodeState.Changed],
        };

    /// <summary>The messages of every error, for an assertion that names what it expects.</summary>
    public static IReadOnlyList<string> ErrorsOf(AnalysisResult result) =>
        AnalysisValidator.Validate(result, Changeset()).ErrorMessages;

    public static AnalysisValidation Check(AnalysisResult result) =>
        AnalysisValidator.Validate(result, Changeset());
}
