using System.Globalization;
using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// Checks a result the model produced against the rules a JSON Schema cannot express.
/// <para>
/// This is the load-bearing piece of the whole application. §0.2.1 says the LLM is the source of
/// truth and the app does not second-guess it; §0.2.5 says every changed file appears in the graph,
/// validated after every run. Those two live together only if the application refuses to
/// <i>silently</i> correct anything: every problem found here is named and handed back, and a
/// result that keeps failing fails the run rather than being quietly patched into shape.
/// </para>
/// <para>
/// Errors and warnings are separated for the same reason. An uncovered file or a dangling edge
/// makes the result unusable and is worth spending a repair round on. A cycle does not: mutual
/// dependencies exist in real code, and rejecting the answer would be asking the model to
/// misdescribe the repository. The reading direction stays unambiguous regardless, because it
/// comes from container order and node rank rather than from following edges.
/// </para>
/// </summary>
public static class AnalysisValidator
{
    /// <summary>
    /// Validates <paramref name="result"/> against the changeset it claims to describe.
    /// </summary>
    /// <param name="result">The model's answer, already known to match the schema.</param>
    /// <param name="changedFiles">Every file in the changeset. The completeness rule is measured against this.</param>
    public static AnalysisValidation Validate(AnalysisResult result, IReadOnlyList<ChangedFile> changedFiles)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(changedFiles);

        var diagnostics = new List<AnalysisDiagnostic>();
        var nodesById = CheckNodeIdentity(result, diagnostics);

        CheckFileCoverage(result, changedFiles, diagnostics, nodesById);
        CheckNodeContent(result, diagnostics);

        var membership = CheckContainers(result, nodesById, diagnostics);

        CheckMembership(result, nodesById, membership, diagnostics);
        CheckEdges(result, nodesById, diagnostics);
        CheckReadingOrder(result, nodesById, diagnostics);
        CheckCycles(result, diagnostics);
        CheckReachability(result, diagnostics);
        CheckFieldLengths(result, diagnostics);

        return new AnalysisValidation { Diagnostics = diagnostics };
    }

    /// <summary>Ids exist, are unique, and are derived from the path they claim.</summary>
    private static Dictionary<string, AnalysisNode> CheckNodeIdentity(
        AnalysisResult result,
        List<AnalysisDiagnostic> diagnostics)
    {
        var nodesById = new Dictionary<string, AnalysisNode>(StringComparer.Ordinal);

        foreach (var node in result.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.NodeIdNotDerived,
                    node.FilePath,
                    $"The node for '{node.FilePath}' has no id. It must be "
                        + AnalysisNodeId.Describe(node.FilePath) + "."));

                continue;
            }

            if (!nodesById.TryAdd(node.Id, node))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.DuplicateNodeId,
                    node.Id,
                    $"Two nodes share the id '{node.Id}'. Ids must be unique: give the second one a "
                        + $"'{AnalysisNodeId.Separator}' suffix naming the part of the file it is about."));
            }
        }

        return nodesById;
    }

    /// <summary>
    /// §0.2.5, in both directions: nothing dropped, and nothing invented either. The second half
    /// matters as much as the first — a node for a file that did not change is the model covering
    /// a gap rather than reporting one.
    /// </summary>
    private static void CheckFileCoverage(
        AnalysisResult result,
        IReadOnlyList<ChangedFile> changedFiles,
        List<AnalysisDiagnostic> diagnostics,
        Dictionary<string, AnalysisNode> nodesById)
    {
        var changedPaths = new HashSet<string>(
            changedFiles.Select(static file => file.Path),
            StringComparer.Ordinal);

        var covered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in result.Nodes)
        {
            covered.Add(node.FilePath);

            if (!changedPaths.Contains(node.FilePath))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.UnknownFile,
                    node.Id,
                    $"Node '{node.Id}' claims the file '{node.FilePath}', which is not in this "
                        + "changeset. Use a path exactly as the changed-file list spells it."));
            }
            else if (nodesById.TryGetValue(node.Id, out var owner) &&
                ReferenceEquals(owner, node) &&
                !AnalysisNodeId.IsDerivedFrom(node.Id, node.FilePath))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.NodeIdNotDerived,
                    node.Id,
                    $"Node id '{node.Id}' is not derived from its file path '{node.FilePath}'. "
                        + "It must be " + AnalysisNodeId.Describe(node.FilePath) + "."));
            }
        }

        foreach (var file in changedFiles)
        {
            if (!covered.Contains(file.Path))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.FileNotCovered,
                    file.Path,
                    $"No node covers the changed file '{file.Path}'. Every file in the changeset "
                        + "must appear in the graph, including deleted and binary ones."));
            }
        }
    }

    /// <summary>
    /// The three fields a node cannot honestly leave empty, and the bounds on importance.
    /// <para>
    /// Deliberately short. A deleted file or a binary the model could not read still has something
    /// true to say about what changed and why, but nothing to say about downstream effects or
    /// implementation detail — so a thin node passes, and only an empty one does not.
    /// </para>
    /// </summary>
    private static void CheckNodeContent(AnalysisResult result, List<AnalysisDiagnostic> diagnostics)
    {
        foreach (var node in result.Nodes)
        {
            foreach (var (field, value) in new[]
            {
                ("title", node.Title),
                ("whatChanged", node.WhatChanged),
                ("whyItChanged", node.WhyItChanged),
            })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    diagnostics.Add(AnalysisDiagnostic.Error(
                        AnalysisDiagnosticCodes.EmptyNodeField,
                        node.Id,
                        $"Node '{node.Id}' has an empty {field}. Even a deleted or binary file has "
                            + "something true to say there."));
                }
            }

            if (node.Importance is < 1 or > 5)
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.ImportanceOutOfRange,
                    node.Id,
                    $"Node '{node.Id}' has importance {node.Importance.ToString(CultureInfo.InvariantCulture)}, "
                        + "which is outside the range 1 to 5."));
            }
        }
    }

    /// <summary>
    /// Container identity, entry nodes and ranks. Returns which containers claim each node, so the
    /// membership rule can be checked once against the whole picture.
    /// </summary>
    private static Dictionary<string, List<string>> CheckContainers(
        AnalysisResult result,
        Dictionary<string, AnalysisNode> nodesById,
        List<AnalysisDiagnostic> diagnostics)
    {
        var membership = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var displayOrders = new List<int>();

        foreach (var container in result.Containers)
        {
            if (!seen.Add(container.Id))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.DuplicateContainerId,
                    container.Id,
                    $"Two containers share the id '{container.Id}'. Container ids must be unique."));
            }

            displayOrders.Add(container.DisplayOrder);

            if (container.NodeIds.Count == 0)
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.EmptyContainer,
                    container.Id,
                    $"Container '{container.Id}' has no nodes. Remove it, or move nodes into it."));
            }

            var members = new List<AnalysisNode>();

            foreach (var nodeId in container.NodeIds)
            {
                if (!membership.TryGetValue(nodeId, out var owners))
                {
                    membership[nodeId] = owners = [];
                }

                owners.Add(container.Id);

                if (nodesById.TryGetValue(nodeId, out var node))
                {
                    members.Add(node);
                }
                else
                {
                    diagnostics.Add(AnalysisDiagnostic.Error(
                        AnalysisDiagnosticCodes.UnknownNodeReference,
                        container.Id,
                        $"Container '{container.Id}' lists the node '{nodeId}', which does not exist."));
                }
            }

            CheckEntryNode(container, nodesById, members, diagnostics);
            CheckRanks(container, members, diagnostics);
        }

        CheckDense(
            displayOrders,
            AnalysisDiagnosticCodes.DisplayOrderNotDense,
            string.Empty,
            "container displayOrder values",
            diagnostics);

        return membership;
    }

    /// <summary>
    /// Exactly one entry node per container, declared and stated consistently.
    /// <para>
    /// Both halves are checked because both are representable and both are wrong on their own: a
    /// container can name an entry node that is not in it, and it can carry two nodes marked
    /// <c>entry_point</c> while naming one of them. A reviewer told to start in two places has not
    /// been told where to start.
    /// </para>
    /// </summary>
    private static void CheckEntryNode(
        AnalysisContainer container,
        Dictionary<string, AnalysisNode> nodesById,
        List<AnalysisNode> members,
        List<AnalysisDiagnostic> diagnostics)
    {
        var declared = container.EntryNodeId;

        if (string.IsNullOrWhiteSpace(declared) || !nodesById.ContainsKey(declared))
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.NoEntryNode,
                container.Id,
                $"Container '{container.Id}' names no entry node that exists. Every container needs "
                    + "exactly one node a reviewer starts from."));
        }
        else if (!container.NodeIds.Contains(declared, StringComparer.Ordinal))
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.EntryNodeNotAMember,
                container.Id,
                $"Container '{container.Id}' names '{declared}' as its entry node, but that node is "
                    + "not one of its members."));
        }
        else if (nodesById[declared].Rank != 1)
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.EntryNodeNotFirst,
                container.Id,
                $"Container '{container.Id}' starts at '{declared}', but that node has rank "
                    + $"{nodesById[declared].Rank.ToString(CultureInfo.InvariantCulture)} rather than 1. "
                    + "The entry node is the one the ranks count from."));
        }

        var marked = members
            .Where(static node => node.States.Contains(AnalysisNodeState.EntryPoint))
            .Select(static node => node.Id)
            .ToArray();

        if (marked.Length > 1)
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.ManyEntryNodes,
                container.Id,
                $"Container '{container.Id}' has {marked.Length.ToString(CultureInfo.InvariantCulture)} "
                    + $"nodes marked entry_point ({string.Join(", ", marked)}). Exactly one may be."));
        }
        else if (marked.Length == 0 && members.Count > 0)
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.NoEntryNode,
                container.Id,
                $"No node in container '{container.Id}' is marked entry_point. Its entry node must "
                    + "carry that state."));
        }
        else if (marked.Length == 1 && !string.Equals(marked[0], declared, StringComparison.Ordinal))
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.EntryStateDisagrees,
                container.Id,
                $"Container '{container.Id}' names '{declared}' as its entry node, but '{marked[0]}' "
                    + "is the node marked entry_point. They must be the same node."));
        }
    }

    private static void CheckRanks(
        AnalysisContainer container,
        List<AnalysisNode> members,
        List<AnalysisDiagnostic> diagnostics) =>
        CheckDense(
            members.Select(static node => node.Rank).ToList(),
            AnalysisDiagnosticCodes.RankNotDense,
            container.Id,
            $"node ranks in container '{container.Id}'",
            diagnostics);

    /// <summary>
    /// Whether a set of ordering numbers is exactly 1..n. Layout intent is only obeyable if it is
    /// a total order: a gap or a repeat leaves the renderer choosing, which is the one thing
    /// §0.2.1 says it must not do.
    /// </summary>
    private static void CheckDense(
        List<int> values,
        string code,
        string subject,
        string what,
        List<AnalysisDiagnostic> diagnostics)
    {
        if (values.Count == 0)
        {
            return;
        }

        var expected = Enumerable.Range(1, values.Count);
        var actual = values.Order().ToArray();

        if (actual.SequenceEqual(expected))
        {
            return;
        }

        diagnostics.Add(AnalysisDiagnostic.Error(
            code,
            subject,
            $"The {what} are {string.Join(", ", actual.Select(static value => value.ToString(CultureInfo.InvariantCulture)))}, "
                + $"but they must be 1 to {values.Count.ToString(CultureInfo.InvariantCulture)} with no gaps and no repeats."));
    }

    /// <summary>Every node in exactly one container — not none, and not two.</summary>
    private static void CheckMembership(
        AnalysisResult result,
        Dictionary<string, AnalysisNode> nodesById,
        Dictionary<string, List<string>> membership,
        List<AnalysisDiagnostic> diagnostics)
    {
        foreach (var node in result.Nodes)
        {
            if (!nodesById.TryGetValue(node.Id, out var owner) || !ReferenceEquals(owner, node))
            {
                // Already reported as a missing or duplicate id; saying it twice helps nobody.
                continue;
            }

            var owners = membership.TryGetValue(node.Id, out var found) ? found : [];

            if (owners.Count == 0)
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.NodeInNoContainer,
                    node.Id,
                    $"Node '{node.Id}' is in no container. Every node belongs to exactly one."));
            }
            else if (owners.Count > 1)
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.NodeInManyContainers,
                    node.Id,
                    $"Node '{node.Id}' is listed by {owners.Count.ToString(CultureInfo.InvariantCulture)} "
                        + $"containers ({string.Join(", ", owners)}). It belongs to exactly one."));
            }
        }
    }

    private static void CheckEdges(
        AnalysisResult result,
        Dictionary<string, AnalysisNode> nodesById,
        List<AnalysisDiagnostic> diagnostics)
    {
        foreach (var edge in result.Edges)
        {
            var name = $"{edge.SourceNodeId} -> {edge.TargetNodeId}";

            foreach (var (end, id) in new[] { ("source", edge.SourceNodeId), ("target", edge.TargetNodeId) })
            {
                if (!nodesById.ContainsKey(id ?? string.Empty))
                {
                    diagnostics.Add(AnalysisDiagnostic.Error(
                        AnalysisDiagnosticCodes.DanglingEdge,
                        name,
                        $"The edge {name} has a {end} node '{id}' that does not exist."));
                }
            }

            if (string.Equals(edge.SourceNodeId, edge.TargetNodeId, StringComparison.Ordinal))
            {
                diagnostics.Add(AnalysisDiagnostic.Warning(
                    AnalysisDiagnosticCodes.SelfEdge,
                    edge.SourceNodeId,
                    $"The node '{edge.SourceNodeId}' has an edge to itself, which says nothing about "
                        + "reading order and is ignored."));
            }
        }
    }

    /// <summary>
    /// The reading order references real nodes and repeats none.
    /// <para>
    /// Not covering every node is a warning rather than an error: container order and rank already
    /// define a complete traversal, so the application can fall back to one it derives rather than
    /// spending a repair round — and money — on a list the model merely cut short.
    /// </para>
    /// </summary>
    private static void CheckReadingOrder(
        AnalysisResult result,
        Dictionary<string, AnalysisNode> nodesById,
        List<AnalysisDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in result.ReadingOrder)
        {
            if (!nodesById.ContainsKey(id))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.UnknownNodeReference,
                    id,
                    $"The reading order names '{id}', which is not a node in this result."));
            }
            else if (!seen.Add(id))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.DuplicateReadingOrderEntry,
                    id,
                    $"The reading order names '{id}' more than once."));
            }
        }

        var missing = result.Nodes.Count - seen.Count;

        if (missing > 0)
        {
            diagnostics.Add(AnalysisDiagnostic.Warning(
                AnalysisDiagnosticCodes.IncompleteReadingOrder,
                string.Empty,
                $"The reading order leaves out {missing.ToString(CultureInfo.InvariantCulture)} node(s); "
                    + "container order and node rank were used instead."));
        }
    }

    /// <summary>
    /// Whether each container is a path a reader can walk from its entry node, or a pile of files
    /// that happen to sit together.
    /// <para>
    /// This is the closest the application comes to checking the thing it exists for. A reviewer
    /// starts at the entry node and follows the change outwards; a node no edge leads to is one they
    /// arrive at cold, having to work out for themselves why it is in front of them — which is the
    /// work this product is supposed to have already done.
    /// </para>
    /// <para>
    /// A warning, not an error. The node may genuinely belong there with the connection merely left
    /// unstated, and failing the run over it would cost a repair round to buy an edge the model
    /// might invent rather than find. Only edges with both ends inside the container count: a
    /// cross-container edge is drawn faintly and kept out of the layout (§0.6), so it is not
    /// something the reader follows to get here.
    /// </para>
    /// </summary>
    private static void CheckReachability(AnalysisResult result, List<AnalysisDiagnostic> diagnostics)
    {
        foreach (var container in result.Containers)
        {
            var members = new HashSet<string>(container.NodeIds, StringComparer.Ordinal);

            if (members.Count < 2 || !members.Contains(container.EntryNodeId))
            {
                continue;
            }

            var onward = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var edge in result.Edges)
            {
                if (members.Contains(edge.SourceNodeId) && members.Contains(edge.TargetNodeId))
                {
                    if (!onward.TryGetValue(edge.SourceNodeId, out var targets))
                    {
                        onward[edge.SourceNodeId] = targets = [];
                    }

                    targets.Add(edge.TargetNodeId);
                }
            }

            var reached = new HashSet<string>(StringComparer.Ordinal) { container.EntryNodeId };
            var queue = new Queue<string>();
            queue.Enqueue(container.EntryNodeId);

            while (queue.Count > 0)
            {
                if (!onward.TryGetValue(queue.Dequeue(), out var targets))
                {
                    continue;
                }

                foreach (var target in targets)
                {
                    if (reached.Add(target))
                    {
                        queue.Enqueue(target);
                    }
                }
            }

            // Reported in the container's own order, so two runs over the same graph say the same
            // thing in the same sequence.
            foreach (var nodeId in container.NodeIds)
            {
                if (!reached.Contains(nodeId))
                {
                    diagnostics.Add(AnalysisDiagnostic.Warning(
                        AnalysisDiagnosticCodes.UnreachableNode,
                        nodeId,
                        $"Nothing leads to '{nodeId}' from '{container.EntryNodeId}', the start of "
                            + $"container '{container.Id}'. A reader reaches it without being told why."));
                }
            }
        }
    }

    private static void CheckCycles(AnalysisResult result, List<AnalysisDiagnostic> diagnostics)
    {
        foreach (var cycle in AnalysisGraph.Build(result.Nodes, result.Edges).Cycles)
        {
            diagnostics.Add(AnalysisDiagnostic.Warning(
                AnalysisDiagnosticCodes.Cycle,
                cycle[0],
                $"These nodes form a cycle in the reading flow: {string.Join(", ", cycle)}."));
        }
    }

    /// <summary>
    /// Fields that ran to more than twice the length they were asked for.
    /// <para>
    /// Warnings, always. The prompt states every budget in <see cref="AnalysisFieldBudgets"/>, and a
    /// model that overshoots has still answered the question — the text is simply longer than the
    /// box it is read in, and the renderer truncates it. Making this an error would send a
    /// three-hundred-node document back to be rewritten because one sentence ran long, and that
    /// repair round costs more than the verbosity does.
    /// </para>
    /// <para>
    /// One diagnostic per offending field rather than one summary, so the subject names what to
    /// shorten. The whole result is walked: capping the count would hide exactly the case worth
    /// seeing, which is a model being verbose everywhere rather than once.
    /// </para>
    /// </summary>
    private static void CheckFieldLengths(AnalysisResult result, List<AnalysisDiagnostic> diagnostics)
    {
        Report(string.Empty, "summary", result.Summary, AnalysisFieldBudgets.OverallSummary);

        foreach (var risk in result.OverallRisks)
        {
            Report(string.Empty, "risk", risk, AnalysisFieldBudgets.Risk);
        }

        foreach (var container in result.Containers)
        {
            Report(container.Id, "title", container.Title, AnalysisFieldBudgets.ContainerTitle);
            Report(container.Id, "summary", container.Summary, AnalysisFieldBudgets.ContainerSummary);
            Report(container.Id, "explanation", container.Explanation, AnalysisFieldBudgets.ContainerExplanation);

            foreach (var risk in container.Risks)
            {
                Report(container.Id, "risk", risk, AnalysisFieldBudgets.Risk);
            }
        }

        foreach (var node in result.Nodes)
        {
            Report(node.Id, "title", node.Title, AnalysisFieldBudgets.NodeTitle);
            Report(node.Id, "whatChanged", node.WhatChanged, AnalysisFieldBudgets.NodeProse);
            Report(node.Id, "whyItChanged", node.WhyItChanged, AnalysisFieldBudgets.NodeProse);
            Report(node.Id, "howItAffectsOthers", node.HowItAffectsOthers, AnalysisFieldBudgets.NodeProse);
            Report(node.Id, "implementationNotes", node.ImplementationNotes, AnalysisFieldBudgets.NodeProse);

            foreach (var risk in node.Risks)
            {
                Report(node.Id, "risk", risk, AnalysisFieldBudgets.Risk);
            }
        }

        void Report(string subject, string field, string? value, int budget)
        {
            if (!AnalysisFieldBudgets.IsOverlong(value, budget))
            {
                return;
            }

            diagnostics.Add(AnalysisDiagnostic.Warning(
                AnalysisDiagnosticCodes.VerboseField,
                subject,
                $"The {field} {(subject.Length == 0 ? "of the change as a whole" : $"of '{subject}'")} is "
                    + $"{value!.Length} characters against a budget of {budget}. It will be shown truncated."));
        }
    }
}

/// <summary>What validation found. Errors fail the run; warnings travel with the stored result.</summary>
public sealed record AnalysisValidation
{
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    public IReadOnlyList<AnalysisDiagnostic> Errors =>
        [.. Diagnostics.Where(static d => d.Severity is AnalysisDiagnosticSeverity.Error)];

    public IReadOnlyList<AnalysisDiagnostic> Warnings =>
        [.. Diagnostics.Where(static d => d.Severity is AnalysisDiagnosticSeverity.Warning)];

    public bool IsValid => !Diagnostics.Any(static d => d.Severity is AnalysisDiagnosticSeverity.Error);

    /// <summary>The error messages, which are what goes back to the model to be repaired.</summary>
    public IReadOnlyList<string> ErrorMessages => [.. Errors.Select(static d => d.Message)];
}
