using System.Globalization;
using DiffHacker.Contracts;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;

// The domain and the wire genuinely have types of the same name here — the model's answer and the
// renderer's view of it describe the same things — so the three the signatures below name are
// aliased rather than left to whichever using directive wins.
using AnalysisFreshnessBasis = DiffHacker.Contracts.AnalysisFreshnessBasis;
using DomainEdge = DiffHacker.Core.Analyses.AnalysisEdge;
using DomainEdgeKind = DiffHacker.Core.Analyses.AnalysisEdgeKind;
using DomainFreshnessBasis = DiffHacker.Core.Analyses.AnalysisFreshnessBasis;
using DomainNode = DiffHacker.Core.Analyses.AnalysisNode;
using DomainNodeState = DiffHacker.Core.Analyses.AnalysisNodeState;
using DomainResult = DiffHacker.Core.Analyses.AnalysisResult;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Domain to wire for the analysis contracts.
/// <para>
/// <b>This is where a grouping is chosen.</b> The stored document holds two groupings of one node
/// set; a view is one of them, and everything that differs between them is derived right here rather
/// than sent twice and switched inside the renderer. Container membership is resolved onto each
/// node, each node is given its rank from where its container lists it, the entry node is given its
/// entry_point state, each edge is told whether it crosses a container boundary, and the four
/// statistics that depend on the grouping are recomputed. Nothing is invented: an edge crosses
/// containers because of where its ends were put in <i>this</i> grouping, not because anyone decided
/// it does — which is exactly why the same edge is drawn faint in one picture and solid in the other.
/// </para>
/// <para>
/// Keeping it here means one projection rather than two implementations of it, and a switch is a
/// call that returns a whole view — the arrangement every other <c>analysis.*</c> method already
/// uses. Re-laying the diagram out dwarfs the cost of that call.
/// </para>
/// <para>
/// The container, node and edge shapes duplicate the ones the model answers in, because a schema
/// cannot reference a definition in another file. <c>AnalysisAgreementTests</c> is what stops the
/// two copies drifting; this is where the duplication is paid for, once.
/// </para>
/// </summary>
internal static class AnalysisWire
{
    /// <summary>
    /// What the renderer is shown when a repository has never been analysed. It still carries
    /// <paramref name="nextRun"/>, because the controls that bind to it are about what the next run
    /// will spend and are on screen before any analysis exists.
    /// </summary>
    public static AnalysisView Empty(string repositoryPath, AnalysisRunOptions nextRun) => new(
        analysisId: null,
        availableGroupings: [AnalysisGroupingMode.Dependency_flow],
        changedFiles: [],
        containers: [],
        costUsd: null,
        createdAtUtc: null,
        diagnostics: [],
        durationMs: null,
        edges: [],
        grouping: AnalysisGroupingMode.Dependency_flow,
        hasAnalysis: false,
        headCommit: null,
        implementationGroups: [],
        implementationGroupsProduced: false,
        inputTokens: null,
        isLatest: false,
        model: null,
        nodes: [],
        outputTokens: null,
        overallRisks: [],
        produceChangeClusters: nextRun.ChangeClusters,
        produceImplementationGroups: nextRun.ImplementationGroups,
        providerDisplayName: null,
        readingOrder: [],
        repairRounds: null,
        repositoryPath: repositoryPath,
        reviewedNodeIds: [],
        schemaVersion: null,
        statistics: null,
        summary: string.Empty,
        toolCallCount: 0);

    /// <param name="analysis">The stored analysis.</param>
    /// <param name="grouping">Which of its groupings to project.</param>
    /// <param name="nextRun">What the next run will ask for — the remembered choices, not this analysis's.</param>
    /// <param name="isLatest">Whether this is the repository's most recent analysis.</param>
    public static AnalysisView ToWire(
        Analysis analysis,
        AnalysisGrouping grouping,
        AnalysisRunOptions nextRun,
        bool isLatest)
    {
        ArgumentNullException.ThrowIfNull(nextRun);
        ArgumentNullException.ThrowIfNull(analysis);

        var document = analysis.Document;
        var view = document.For(grouping);

        var containerOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var entryNodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var container in view.Containers)
        {
            order[container.Id] = container.DisplayOrder;
            entryNodeIds.Add(container.EntryNodeId);

            for (var position = 0; position < container.NodeIds.Count; position++)
            {
                var nodeId = container.NodeIds[position];
                containerOf[nodeId] = container.Id;

                // The order the container listed its members in, turned into the number the renderer
                // was written against. The first is 1, and validation makes that the entry node.
                ranks[nodeId] = position + 1;
            }
        }

        return new AnalysisView(
            analysisId: analysis.Id,
            availableGroupings: [.. analysis.AvailableGroupings.Select(ToWire)],
            changedFiles:
            [
                .. analysis.ChangedFiles.Select(static file => new ChangedFileFactsInfo(
                    isBinary: file.IsBinary,
                    language: file.Language,
                    linesAdded: file.LinesAdded,
                    linesRemoved: file.LinesRemoved,
                    path: file.Path,
                    previousPath: file.PreviousPath,
                    project: file.Project,
                    status: ToWire(file.Status))),
            ],
            containers:
            [
                .. view.Containers
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
                // Only what describes the picture on screen: the observations about the change as a
                // whole, plus the ones about this grouping. A warning naming a cluster the reviewer
                // is not looking at is one they cannot act on.
                .. analysis.Diagnostics
                    .Where(diagnostic => diagnostic.Grouping is null || diagnostic.Grouping == grouping)
                    .Select(static diagnostic => new AnalysisDiagnosticInfo(
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
            grouping: ToWire(grouping),
            hasAnalysis: true,
            headCommit: analysis.HeadCommit,
            implementationGroups: [.. ImplementationGroupsIn(document, containerOf, ranks)],
            implementationGroupsProduced: document.ImplementationGroups is not null,
            inputTokens: Saturate(analysis.Usage.InputTokens),
            isLatest: isLatest,
            model: analysis.Model,
            nodes:
            [
                // Sorted the way the diagram is read — container order, then rank — so the renderer
                // is handed the layout intent rather than having to reconstruct it. Both keys belong
                // to the active grouping, so switching reorders the list.
                .. document.Nodes
                    .OrderBy(node => containerOf.TryGetValue(node.Id, out var id) && order.TryGetValue(id, out var at)
                        ? at
                        : int.MaxValue)
                    .ThenBy(node => ranks.TryGetValue(node.Id, out var rank) ? rank : int.MaxValue)
                    .ThenBy(static node => node.Id, StringComparer.Ordinal)
                    .Select(node => new AnalysisNodeInfo(
                        containerId: containerOf.TryGetValue(node.Id, out var containerId) ? containerId : string.Empty,
                        endLine: node.EndLine,
                        filePath: node.FilePath,
                        howItAffectsOthers: node.HowItAffectsOthers,
                        id: node.Id,
                        implementationNotes: node.ImplementationNotes,
                        importance: node.Importance,
                        rank: ranks.TryGetValue(node.Id, out var nodeRank) ? nodeRank : 0,
                        risks: node.Risks,
                        startLine: node.StartLine,
                        states: [.. StatesOf(node, entryNodeIds)],
                        symbol: node.Symbol,
                        title: node.Title,
                        whatChanged: node.WhatChanged,
                        whyItChanged: node.WhyItChanged)),
            ],
            outputTokens: Saturate(analysis.Usage.OutputTokens),
            overallRisks: document.OverallRisks,
            produceChangeClusters: nextRun.ChangeClusters,
            produceImplementationGroups: nextRun.ImplementationGroups,
            providerDisplayName: analysis.ProviderDisplayName,
            readingOrder: analysis.ReadingOrderFor(grouping),
            repairRounds: analysis.RepairRounds,
            repositoryPath: analysis.RepositoryPath,
            reviewedNodeIds: analysis.ReviewedNodeIds,
            schemaVersion: analysis.SchemaVersion,
            statistics: ToWire(analysis.StatisticsFor(grouping)),
            summary: document.Summary,
            toolCallCount: analysis.ToolCalls.Count);
    }

    /// <summary>
    /// The library. Summaries arrive most recent first, so the first one is the latest — the same
    /// rule <c>GetLatestAsync</c> applies, stated once more rather than asked of the store again.
    /// </summary>
    public static AnalysisLibrary ToLibrary(string repositoryPath, IReadOnlyList<AnalysisSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        return new AnalysisLibrary(
            entries:
            [
                .. summaries.Select((summary, index) => new AnalysisLibraryEntry(
                    analysisId: summary.Id,
                    containerCount: summary.ContainerCount,
                    costUsd: summary.Usage.EstimatedCostUsd?.ToString(CultureInfo.InvariantCulture),
                    createdAtUtc: summary.CreatedAtUtc,
                    durationMs: (int)Math.Min(summary.Duration.TotalMilliseconds, int.MaxValue),
                    fileCount: summary.FileCount,
                    headCommit: summary.HeadCommit,
                    inputTokens: Saturate(summary.Usage.InputTokens),
                    isLatest: index == 0,
                    linesAdded: summary.LinesAdded,
                    linesRemoved: summary.LinesRemoved,
                    model: summary.Model,
                    nodeCount: summary.NodeCount,
                    outputTokens: Saturate(summary.Usage.OutputTokens),
                    providerDisplayName: summary.ProviderDisplayName,
                    toolCallCount: summary.ToolCallCount)),
            ],
            repositoryPath: repositoryPath,
            retentionLimit: AnalysisLibraryPolicy.RetentionLimit);
    }

    /// <summary>
    /// The recorded trace, in the order the model asked for the calls. Sorted by ordinal here even
    /// though the store keeps them in that order already: "in order" is the requirement, and it
    /// should not rest on how a JSON array happened to be written.
    /// </summary>
    public static AnalysisTrace ToTrace(Analysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        return new AnalysisTrace(
            analysisId: analysis.Id,
            progressMessages: analysis.ProgressMessages,
            toolCalls:
            [
                .. analysis.ToolCalls
                    .OrderBy(static call => call.Ordinal)
                    .Select(static call => new AnalysisToolCallInfo(
                        argumentsPreview: call.ArgumentsPreview,
                        durationMs: call.Duration.TotalMilliseconds,
                        isError: call.IsError,
                        ordinal: call.Ordinal,
                        resultBytes: call.ResultBytes,
                        resultPreview: call.ResultPreview,
                        toolName: call.ToolName,
                        turn: call.Turn)),
            ]);
    }

    /// <summary>
    /// How many paths of each kind travel. The counts are always whole; the lists are for a banner
    /// a person reads, and a thousand paths in one is not something anyone reads.
    /// </summary>
    public const int FreshnessPathLimit = 50;

    public static AnalysisFreshness ToWire(AnalysisFreshnessReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new AnalysisFreshness(
            addedCount: report.Added.Count,
            addedPaths: [.. report.Added.Take(FreshnessPathLimit)],
            analysisId: report.AnalysisId,
            basis: report.Basis switch
            {
                DomainFreshnessBasis.Content => AnalysisFreshnessBasis.Content,
                DomainFreshnessBasis.LineCounts => AnalysisFreshnessBasis.Line_counts,
                DomainFreshnessBasis.HeadOnly => AnalysisFreshnessBasis.Head_only,
                _ => throw new ArgumentOutOfRangeException(nameof(report), report.Basis, "Unmapped basis."),
            },
            checkedAtUtc: report.CheckedAtUtc,
            currentHeadCommit: report.CurrentHeadCommit,
            headMoved: report.HeadMoved,
            isStale: report.IsStale,
            modifiedCount: report.Modified.Count,
            modifiedPaths: [.. report.Modified.Take(FreshnessPathLimit)],
            recordedHeadCommit: report.RecordedHeadCommit,
            removedCount: report.Removed.Count,
            removedPaths: [.. report.Removed.Take(FreshnessPathLimit)]);
    }

    /// <summary>
    /// A node's states, plus <c>entry_point</c> when it starts a container of the active grouping.
    /// <para>
    /// The model no longer says this, and could not: with two groupings a node may start a cluster in
    /// one and sit in the middle of another, so one state on the node had no honest value to hold.
    /// Derived here instead, from the single place a grouping declares it.
    /// </para>
    /// </summary>
    private static IEnumerable<AnalysisNodeInfoState> StatesOf(
        DomainNode node,
        HashSet<string> entryNodeIds)
    {
        foreach (var state in node.States)
        {
            yield return ToWire(state);
        }

        if (entryNodeIds.Contains(node.Id))
        {
            yield return AnalysisNodeInfoState.Entry_point;
        }
    }

    /// <summary>
    /// The model's implementation groups as they fall in the active grouping.
    /// <para>
    /// A group is drawn inside one container, so it lives in its abstraction's container and keeps
    /// only the implementations the same container holds, in the order that container reads them.
    /// The rest are not dropped from anything — they are still nodes, drawn as boxes of their own —
    /// and a group left with no implementation here has nothing to merge in this grouping. Which one
    /// that is can differ between groupings, which is why it is decided here and not stored.
    /// </para>
    /// </summary>
    private static IEnumerable<AnalysisImplementationGroupInfo> ImplementationGroupsIn(
        DomainResult document,
        Dictionary<string, string> containerOf,
        Dictionary<string, int> ranks)
    {
        foreach (var group in document.ImplementationGroups ?? [])
        {
            if (!containerOf.TryGetValue(group.AbstractionNodeId, out var home))
            {
                continue;
            }

            string[] beside =
            [
                .. group.ImplementationNodeIds
                    .Where(id => !string.Equals(id, group.AbstractionNodeId, StringComparison.Ordinal))
                    .Where(id => containerOf.TryGetValue(id, out var at) && string.Equals(at, home, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => ranks.TryGetValue(id, out var rank) ? rank : int.MaxValue),
            ];

            if (beside.Length > 0)
            {
                yield return new AnalysisImplementationGroupInfo(
                    abstractionNodeId: group.AbstractionNodeId,
                    containerId: home,
                    implementationNodeIds: beside);
            }
        }
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
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped node state."),
    };

    /// <inheritdoc cref="ToWire(DomainNodeState)"/>
    private static AnalysisGroupingMode ToWire(AnalysisGrouping grouping) => grouping switch
    {
        AnalysisGrouping.DependencyFlow => AnalysisGroupingMode.Dependency_flow,
        AnalysisGrouping.ChangeClusters => AnalysisGroupingMode.Change_clusters,
        _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "Unmapped grouping."),
    };

    /// <summary>
    /// The other direction, for the one request that names a grouping. Exhaustive for the same reason
    /// as the rest: a wire value with no domain meaning should fail here rather than be guessed at.
    /// </summary>
    public static AnalysisGrouping FromWire(SetGroupingMode grouping) => grouping switch
    {
        SetGroupingMode.Dependency_flow => AnalysisGrouping.DependencyFlow,
        SetGroupingMode.Change_clusters => AnalysisGrouping.ChangeClusters,
        _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "Unmapped grouping."),
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
