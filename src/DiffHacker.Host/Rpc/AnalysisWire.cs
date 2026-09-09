using System.Globalization;
using DiffHacker.Contracts;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;

// The domain and the wire genuinely have types of the same name here — the model's answer and the
// renderer's view of it describe the same things — so the three the signatures below name are
// aliased rather than left to whichever using directive wins.
using DomainEdge = DiffHacker.Core.Analyses.AnalysisEdge;
using DomainEdgeKind = DiffHacker.Core.Analyses.AnalysisEdgeKind;
using DomainNodeState = DiffHacker.Core.Analyses.AnalysisNodeState;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Domain to wire for the analysis contracts.
/// <para>
/// Two things happen here that the model was deliberately not asked to do. Container membership is
/// resolved onto each node, and each edge is told whether it crosses a container boundary — both
/// derivable from what the model said, and both things the renderer would otherwise recompute on
/// every frame. Nothing is invented: an edge crosses containers because of where its ends were put,
/// not because anyone decided it does.
/// </para>
/// <para>
/// The container, node and edge shapes duplicate the ones the model answers in, because a schema
/// cannot reference a definition in another file. <c>AnalysisAgreementTests</c> is what stops the
/// two copies drifting; this is where the duplication is paid for, once.
/// </para>
/// </summary>
internal static class AnalysisWire
{
    /// <summary>What the renderer is shown when a repository has never been analysed.</summary>
    public static AnalysisView Empty(string repositoryPath) => new(
        analysisId: null,
        changedFiles: [],
        containers: [],
        costUsd: null,
        createdAtUtc: null,
        diagnostics: [],
        durationMs: null,
        edges: [],
        hasAnalysis: false,
        headCommit: null,
        inputTokens: null,
        model: null,
        nodes: [],
        outputTokens: null,
        overallRisks: [],
        providerDisplayName: null,
        readingOrder: [],
        repairRounds: null,
        repositoryPath: repositoryPath,
        schemaVersion: null,
        statistics: null,
        summary: string.Empty);

    public static AnalysisView ToWire(Analysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var document = analysis.Document;

        var containerOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var container in document.Containers)
        {
            order[container.Id] = container.DisplayOrder;

            foreach (var nodeId in container.NodeIds)
            {
                containerOf[nodeId] = container.Id;
            }
        }

        var ranks = document.Nodes.ToDictionary(
            static node => node.Id,
            static node => node.Rank,
            StringComparer.Ordinal);

        return new AnalysisView(
            analysisId: analysis.Id,
            changedFiles:
            [
                .. analysis.ChangedFiles.Select(static file => new ChangedFileFactsInfo(
                    isBinary: file.IsBinary,
                    language: file.Language,
                    linesAdded: file.LinesAdded,
                    linesRemoved: file.LinesRemoved,
                    path: file.Path,
                    project: file.Project,
                    status: ToWire(file.Status))),
            ],
            containers:
            [
                .. document.Containers
                    .OrderBy(static container => container.DisplayOrder)
                    .Select(static container => new AnalysisContainerInfo(
                        displayOrder: container.DisplayOrder,
                        entryNodeId: container.EntryNodeId,
                        explanation: container.Explanation,
                        id: container.Id,
                        nodeIds: container.NodeIds,
                        risks: container.Risks,
                        summary: container.Summary,
                        title: container.Title)),
            ],
            costUsd: analysis.Usage.EstimatedCostUsd?.ToString(CultureInfo.InvariantCulture),
            createdAtUtc: analysis.CreatedAtUtc,
            diagnostics:
            [
                .. analysis.Diagnostics.Select(static diagnostic => new AnalysisDiagnosticInfo(
                    code: diagnostic.Code,
                    message: diagnostic.Message,
                    severity: ToWire(diagnostic.Severity),
                    subject: diagnostic.Subject)),
            ],
            durationMs: (int)analysis.Duration.TotalMilliseconds,
            edges:
            [
                .. document.Edges.Select(edge => new AnalysisEdgeInfo(
                    crossesContainers: CrossesContainers(containerOf, edge),
                    explanation: edge.Explanation,
                    kind: ToWire(edge.Kind),
                    risks: edge.Risks,
                    sourceNodeId: edge.SourceNodeId,
                    targetNodeId: edge.TargetNodeId)),
            ],
            hasAnalysis: true,
            headCommit: analysis.HeadCommit,
            inputTokens: Saturate(analysis.Usage.InputTokens),
            model: analysis.Model,
            nodes:
            [
                // Sorted the way the diagram is read — container order, then rank — so the renderer
                // is handed the layout intent rather than having to reconstruct it.
                .. document.Nodes
                    .OrderBy(node => containerOf.TryGetValue(node.Id, out var id) && order.TryGetValue(id, out var at)
                        ? at
                        : int.MaxValue)
                    .ThenBy(node => ranks[node.Id])
                    .ThenBy(static node => node.Id, StringComparer.Ordinal)
                    .Select(node => new AnalysisNodeInfo(
                        containerId: containerOf.TryGetValue(node.Id, out var containerId) ? containerId : string.Empty,
                        endLine: node.EndLine,
                        filePath: node.FilePath,
                        howItAffectsOthers: node.HowItAffectsOthers,
                        id: node.Id,
                        implementationNotes: node.ImplementationNotes,
                        importance: node.Importance,
                        rank: node.Rank,
                        risks: node.Risks,
                        startLine: node.StartLine,
                        states: [.. node.States.Select(ToWire)],
                        symbol: node.Symbol,
                        title: node.Title,
                        whatChanged: node.WhatChanged,
                        whyItChanged: node.WhyItChanged)),
            ],
            outputTokens: Saturate(analysis.Usage.OutputTokens),
            overallRisks: document.OverallRisks,
            providerDisplayName: analysis.ProviderDisplayName,
            readingOrder: analysis.ReadingOrder,
            repairRounds: analysis.RepairRounds,
            repositoryPath: analysis.RepositoryPath,
            schemaVersion: analysis.SchemaVersion,
            statistics: ToWire(analysis.Statistics),
            summary: document.Summary);
    }

    private static bool CrossesContainers(Dictionary<string, string> containerOf, DomainEdge edge) =>
        containerOf.TryGetValue(edge.SourceNodeId, out var source) &&
        containerOf.TryGetValue(edge.TargetNodeId, out var target) &&
        !string.Equals(source, target, StringComparison.Ordinal);

    private static AnalysisStatsInfo ToWire(AnalysisStatistics statistics)
    {
        var changeset = statistics.Changeset;

        return new AnalysisStatsInfo(
            addedFiles: changeset.ByStatus.Added,
            binaryFiles: changeset.BinaryFiles,
            conceptualEdgeCount: statistics.ConceptualEdgeCount,
            containerCount: statistics.ContainerCount,
            copiedFiles: changeset.ByStatus.Copied,
            deletedFiles: changeset.ByStatus.Deleted,
            directEdgeCount: statistics.DirectEdgeCount,
            edgeCount: statistics.EdgeCount,
            highestFanIn: statistics.HighestFanIn,
            highestFanInNodeId: statistics.HighestFanInNodeId,
            highestFanOut: statistics.HighestFanOut,
            highestFanOutNodeId: statistics.HighestFanOutNodeId,
            languages: changeset.Languages,
            largestContainerSize: statistics.LargestContainerSize,
            longestChain: statistics.LongestChain,
            modifiedFiles: changeset.ByStatus.Modified,
            nodeCount: statistics.NodeCount,
            projects: changeset.Projects,
            renamedFiles: changeset.ByStatus.Renamed,
            riskCount: statistics.RiskCount,
            riskyNodeCount: statistics.RiskyNodeCount,
            smallestContainerSize: statistics.SmallestContainerSize,
            totalFiles: changeset.TotalFiles,
            totalLinesAdded: changeset.TotalLinesAdded,
            totalLinesRemoved: changeset.TotalLinesRemoved);
    }

    /// <remarks>
    /// Exhaustive rather than a cast. The wire values are the contract, and a value added to the
    /// domain enum without a wire spelling should fail here rather than reach the renderer as a
    /// number nobody can render.
    /// </remarks>
    private static AnalysisNodeInfoState ToWire(DomainNodeState state) => state switch
    {
        DomainNodeState.Changed => AnalysisNodeInfoState.Changed,
        DomainNodeState.Added => AnalysisNodeInfoState.Added,
        DomainNodeState.Deleted => AnalysisNodeInfoState.Deleted,
        DomainNodeState.UnchangedRelevant => AnalysisNodeInfoState.Unchanged_relevant,
        DomainNodeState.Risky => AnalysisNodeInfoState.Risky,
        DomainNodeState.EntryPoint => AnalysisNodeInfoState.Entry_point,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped node state."),
    };

    /// <inheritdoc cref="ToWire(DomainNodeState)"/>
    private static AnalysisEdgeInfoKind ToWire(DomainEdgeKind kind) => kind switch
    {
        DomainEdgeKind.Direct => AnalysisEdgeInfoKind.Direct,
        DomainEdgeKind.Conceptual => AnalysisEdgeInfoKind.Conceptual,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped edge kind."),
    };

    /// <inheritdoc cref="ToWire(DomainNodeState)"/>
    private static ChangedFileFactsInfoStatus ToWire(ChangeStatus status) => status switch
    {
        ChangeStatus.Added => ChangedFileFactsInfoStatus.Added,
        ChangeStatus.Modified => ChangedFileFactsInfoStatus.Modified,
        ChangeStatus.Deleted => ChangedFileFactsInfoStatus.Deleted,
        ChangeStatus.Renamed => ChangedFileFactsInfoStatus.Renamed,
        ChangeStatus.Copied => ChangedFileFactsInfoStatus.Copied,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped change status."),
    };

    /// <inheritdoc cref="ToWire(DomainNodeState)"/>
    private static AnalysisDiagnosticInfoSeverity ToWire(AnalysisDiagnosticSeverity severity) => severity switch
    {
        AnalysisDiagnosticSeverity.Error => AnalysisDiagnosticInfoSeverity.Error,
        AnalysisDiagnosticSeverity.Warning => AnalysisDiagnosticInfoSeverity.Warning,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unmapped severity."),
    };

    /// <summary>
    /// Token counts are <c>long</c> in the domain and <c>int</c> on the wire. Saturating rather
    /// than wrapping, the same choice <see cref="RunEventNotifier"/> makes: a run that somehow spent
    /// two billion tokens should read as enormous, not as negative.
    /// </summary>
    private static int Saturate(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
