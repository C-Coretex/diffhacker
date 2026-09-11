using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Llm;

namespace DiffHacker.Host.Tests;

/// <summary>
/// A completed analysis to map onto the wire. Small enough to assert on field by field, and
/// deliberately carrying the awkward parts: a state whose wire spelling differs from its C# name,
/// a conceptual edge, a warning diagnostic, a node the model marked as unchanged, and a binary file
/// whose line counts are absent rather than zero.
/// <para>
/// It holds both groupings, arranged so the one edge in it sits <i>inside</i> a cluster in
/// dependency flow and <i>across</i> two in change clusters. That is what the two views cost and buy
/// respectively, and it means a test can tell the two projections apart by more than a label.
/// </para>
/// <para>
/// Its one implementation group falls the same way: both members share a cluster in dependency flow,
/// so the group is there to merge, and are split in change clusters, so it is not.
/// </para>
/// </summary>
internal static class AnalysisSamples
{
    public static Analysis Completed(
        string repositoryPath,
        bool changeClusters = true,
        bool implementationGroups = true)
    {
        var document = new AnalysisResult
        {
            Summary = "The contract grew a field.",
            OverallRisks = ["Nothing was added to the tests."],
            DependencyReadingOrder = ["src/Contract.cs", "src/Caller.cs"],
            DependencyContainers =
            [
                new AnalysisContainer
                {
                    Id = "core",
                    Title = "The contract",
                    Summary = "A field arrived.",
                    Explanation = "And its caller followed.",
                    DisplayOrder = 1,
                    EntryNodeId = "src/Contract.cs",
                    NodeIds = ["src/Contract.cs", "src/Caller.cs"],
                },
            ],
            ClusterReadingOrder = changeClusters ? ["src/Caller.cs", "src/Contract.cs"] : [],
            ClusterContainers = changeClusters
                ?
                [
                    new AnalysisContainer
                    {
                        Id = "call-sites",
                        Title = "Call sites",
                        Summary = "Where the contract is constructed.",
                        Explanation = "Grouped by concern rather than by the path through it.",
                        DisplayOrder = 1,
                        EntryNodeId = "src/Caller.cs",
                        NodeIds = ["src/Caller.cs"],
                    },
                    new AnalysisContainer
                    {
                        Id = "contracts",
                        Title = "Contracts",
                        Summary = "The shape everything agrees on.",
                        Explanation = "Its own concern in this grouping.",
                        DisplayOrder = 2,
                        EntryNodeId = "src/Contract.cs",
                        NodeIds = ["src/Contract.cs"],
                    },
                ]
                : [],
            Nodes =
            [
                new AnalysisNode
                {
                    Id = "src/Caller.cs",
                    FilePath = "src/Caller.cs",
                    Title = "The caller",
                    WhatChanged = "Nothing, but it has to be read.",
                    WhyItChanged = "It constructs the contract.",
                    Importance = 2,
                    States = [AnalysisNodeState.UnchangedRelevant],
                },
                new AnalysisNode
                {
                    Id = "src/Contract.cs",
                    FilePath = "src/Contract.cs",
                    Title = "The contract",
                    WhatChanged = "A tenant field.",
                    WhyItChanged = "Callers need to pass one.",
                    Importance = 5,
                    States = [AnalysisNodeState.Changed],
                },
            ],
            Edges =
            [
                new AnalysisEdge
                {
                    SourceNodeId = "src/Contract.cs",
                    TargetNodeId = "src/Caller.cs",
                    Kind = AnalysisEdgeKind.Conceptual,
                    Explanation = "Read the decision before its consequence.",
                },
            ],
            ImplementationGroups = implementationGroups
                ?
                [
                    new AnalysisImplementationGroup
                    {
                        AbstractionNodeId = "src/Contract.cs",
                        ImplementationNodeIds = ["src/Caller.cs"],
                    },
                ]
                : null,
        };

        var changed = new[]
        {
            new ChangedFile
            {
                Path = "src/Contract.cs",
                Status = ChangeStatus.Modified,
                LinesAdded = 12,
                LinesRemoved = 3,
                IsBinary = false,
                Language = "C#",
                Project = new ProjectReference("DiffHacker", "src", "DiffHacker.csproj"),
            },
            new ChangedFile
            {
                Path = "src/Caller.cs",
                Status = ChangeStatus.Modified,
                LinesAdded = 4,
                LinesRemoved = 1,
                IsBinary = false,
                Language = "C#",
                Project = new ProjectReference("DiffHacker", "src", "DiffHacker.csproj"),
            },
            new ChangedFile
            {
                // No line counts at all, deliberately. Absent is a different claim from zero, and
                // this is the file that proves the difference survives to the renderer.
                Path = "assets/icon.png",
                Status = ChangeStatus.Deleted,
                IsBinary = true,
                Project = new ProjectReference("assets", "assets", null),
            },
        };

        return new Analysis
        {
            Id = "analysis1",
            RepositoryPath = repositoryPath,
            SchemaVersion = Contracts.ContractVersion.Current,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            HeadCommit = "head0001",
            ProviderDisplayName = "Test",
            Model = "gpt-4o",
            Usage = new LlmUsage
            {
                InputTokens = 1000,
                OutputTokens = 200,
                IsReported = true,
                EstimatedCostUsd = 0.25m,
            },
            Duration = TimeSpan.FromSeconds(42),
            RepairRounds = 1,
            Document = document,
            Statistics = AnalysisStatistics.From(
                document,
                AnalysisGrouping.DependencyFlow,
                ChangesetStatistics.From(changed),
                AnalysisGraph.Build(document.Nodes, document.Edges)),
            Diagnostics =
            [
                // One about the change as a whole and one about a single grouping, so a test can
                // tell that the projection shows the reviewer warnings about the picture they are
                // looking at rather than all of them.
                AnalysisDiagnostic.Warning(
                    AnalysisDiagnosticCodes.Cycle,
                    "src/Caller.cs",
                    "These nodes form a cycle in the reading flow: src/Caller.cs, src/Contract.cs."),
                AnalysisDiagnostic.Warning(
                    AnalysisDiagnosticCodes.UnreachableNode,
                    "src/Caller.cs",
                    "In the change-clusters grouping, nothing leads to 'src/Caller.cs'.",
                    AnalysisGrouping.ChangeClusters),
            ],
            ChangedFiles = [.. changed.Select(ChangedFileFacts.From)],
            ToolCalls = [],
            ProgressMessages = ["Reading the contract"],
        };
    }
}
