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
/// comes from container order and membership order rather than from following edges.
/// </para>
/// <para>
/// <b>Since Iteration 11 the checks divide in two.</b> The node set, the edges and the prose are
/// shared by both groupings and are checked once. Clusters, entry points and reading orders exist
/// once <i>per grouping</i>, and every rule about them runs again for each grouping the result
/// holds — §0.2.5 applies to both pictures, not to whichever one the reviewer happens to open. Each
/// per-grouping message names its grouping, because "no node in container 'auth'" is not something a
/// model can act on when there are two answers it could be about.
/// </para>
/// </summary>
public static class AnalysisValidator
{
    /// <summary>
    /// Validates <paramref name="result"/> against the changeset it claims to describe.
    /// </summary>
    /// <param name="result">The model's answer, already known to match the schema.</param>
    /// <param name="changedFiles">Every file in the changeset. The completeness rule is measured against this.</param>
    /// <param name="options">
    /// What the run asked for. A part it did not ask for was not in the schema the model answered, so
    /// its absence is not a finding: no second grouping, no implementation groups, and no node prose
    /// to insist on. The length warnings are measured against the run's verbosity. Implementation
    /// groups that are present are checked either way.
    /// </param>
    public static AnalysisValidation Validate(
        AnalysisResult result,
        IReadOnlyList<ChangedFile> changedFiles,
        AnalysisRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(changedFiles);
        ArgumentNullException.ThrowIfNull(options);

        var budgets = AnalysisFieldBudgets.For(options.Verbosity);
        var diagnostics = new List<AnalysisDiagnostic>();
        var nodesById = CheckNodeIdentity(result, diagnostics);

        CheckFileCoverage(result, changedFiles, diagnostics, nodesById);
        CheckNodeContent(result, options.NodeExplanations, diagnostics);
        CheckEdges(result, nodesById, diagnostics);
        CheckCycles(result, diagnostics);
        CheckSharedFieldLengths(result, budgets, diagnostics);

        if (options.ChangeClusters && result.ClusterContainers.Count == 0)
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.GroupingMissing,
                string.Empty,
                "The change-clusters grouping is empty. The same nodes have to be grouped a second "
                    + "way, by theme or concern, with every node in exactly one cluster there too.",
                AnalysisGrouping.ChangeClusters));
        }

        if (options.ImplementationGroups && result.ImplementationGroups is null)
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.ImplementationGroupsMissing,
                string.Empty,
                "implementationGroups is missing. List each changed abstraction with its changed "
                    + "implementations, or give an empty list when there are none."));
        }

        CheckImplementationGroups(result, nodesById, diagnostics);

        foreach (var grouping in result.Groupings)
        {
            var view = result.For(grouping);
            var membership = CheckContainers(view, nodesById, diagnostics);

            CheckMembership(result, nodesById, membership, grouping, diagnostics);
            CheckReadingOrder(result, view, nodesById, diagnostics);
            CheckReachability(result, view, diagnostics);
            CheckContainerFieldLengths(view, budgets, diagnostics);
            CheckImplementationGroupsTogether(result, view, diagnostics);
        }

        return new AnalysisValidation { Diagnostics = diagnostics };
    }

    /// <summary>
    /// Each implementation group names real nodes, has something to group, and claims no node
    /// another group — or it — already claimed.
    /// <para>
    /// The overlap rule is what lets a node be drawn in exactly one box. A node in two groups, or
    /// named as its own implementation, is not something the diagram could draw without choosing,
    /// and choosing is the one thing §0.2.1 says the application does not do.
    /// </para>
    /// </summary>
    private static void CheckImplementationGroups(
        AnalysisResult result,
        Dictionary<string, AnalysisNode> nodesById,
        List<AnalysisDiagnostic> diagnostics)
    {
        if (result.ImplementationGroups is not { } groups)
        {
            return;
        }

        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var name = group.AbstractionNodeId;

            if (group.ImplementationNodeIds.Count == 0)
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.ImplementationGroupEmpty,
                    name,
                    $"The implementation group for '{name}' lists no implementations. Name the changed "
                        + "nodes implementing it, or remove the group."));
            }

            foreach (var nodeId in group.ImplementationNodeIds.Prepend(group.AbstractionNodeId))
            {
                if (!nodesById.ContainsKey(nodeId ?? string.Empty))
                {
                    diagnostics.Add(AnalysisDiagnostic.Error(
                        AnalysisDiagnosticCodes.UnknownNodeReference,
                        name,
                        $"The implementation group for '{name}' names '{nodeId}', which is not a node "
                            + "in this result."));

                    continue;
                }

                if (!claimed.TryAdd(nodeId!, name))
                {
                    var owner = claimed[nodeId!];

                    diagnostics.Add(AnalysisDiagnostic.Error(
                        AnalysisDiagnosticCodes.ImplementationGroupOverlap,
                        nodeId!,
                        string.Equals(owner, name, StringComparison.Ordinal)
                            ? $"The implementation group for '{name}' names '{nodeId}' more than once. "
                                + "An abstraction is never its own implementation, and each node is "
                                + "listed once."
                            : $"'{nodeId}' is in the implementation groups of both '{owner}' and "
                                + $"'{name}'. A node belongs to at most one group."));
                }
            }
        }
    }

    /// <summary>
    /// Whether each group's members share the abstraction's container in one grouping.
    /// <para>
    /// A warning, not an error. A member left outside is simply drawn as its own box in that
    /// grouping, which is a smaller picture rather than a wrong one — and a change-clusters grouping
    /// may have a genuine reason to split them. A repair round would cost more than that.
    /// </para>
    /// </summary>
    private static void CheckImplementationGroupsTogether(
        AnalysisResult result,
        AnalysisGroupingView view,
        List<AnalysisDiagnostic> diagnostics)
    {
        if (result.ImplementationGroups is not { Count: > 0 } groups)
        {
            return;
        }

        var containerOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var container in view.Containers)
        {
            foreach (var nodeId in container.NodeIds)
            {
                containerOf.TryAdd(nodeId, container.Id);
            }
        }

        foreach (var group in groups)
        {
            if (!containerOf.TryGetValue(group.AbstractionNodeId, out var home))
            {
                // A node in no container is already an error of its own.
                continue;
            }

            foreach (var nodeId in group.ImplementationNodeIds)
            {
                if (containerOf.TryGetValue(nodeId, out var elsewhere) &&
                    !string.Equals(elsewhere, home, StringComparison.Ordinal))
                {
                    diagnostics.Add(AnalysisDiagnostic.Warning(
                        AnalysisDiagnosticCodes.ImplementationGroupSplit,
                        nodeId,
                        $"{At(view.Grouping)}'{nodeId}' implements '{group.AbstractionNodeId}' but sits in "
                            + $"container '{elsewhere}' rather than '{home}', so it is drawn apart from it.",
                        view.Grouping));
                }
            }
        }
    }

    /// <summary>
    /// How a per-grouping message opens, so the model is never told about "container 'auth'" without
    /// being told which of its two answers that container is in.
    /// </summary>
    private static string At(AnalysisGrouping grouping) => grouping switch
    {
        AnalysisGrouping.DependencyFlow => "In the dependency-flow grouping, ",
        AnalysisGrouping.ChangeClusters => "In the change-clusters grouping, ",
        _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "Unnamed grouping."),
    };

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
    /// <para>
    /// A run that did not ask for node explanations had no prose fields in its schema, so only the
    /// title is insisted on there.
    /// </para>
    /// </summary>
    private static void CheckNodeContent(
        AnalysisResult result,
        bool expectProse,
        List<AnalysisDiagnostic> diagnostics)
    {
        foreach (var node in result.Nodes)
        {
            var required = expectProse
                ? new[] { ("title", node.Title), ("whatChanged", node.WhatChanged), ("whyItChanged", node.WhyItChanged) }
                : [("title", node.Title)];

            foreach (var (field, value) in required)
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
    /// Container identity, entry nodes and membership order, for one grouping. Returns which
    /// containers claim each node, so the membership rule can be checked once against the whole
    /// picture.
    /// </summary>
    private static Dictionary<string, List<string>> CheckContainers(
        AnalysisGroupingView view,
        Dictionary<string, AnalysisNode> nodesById,
        List<AnalysisDiagnostic> diagnostics)
    {
        var membership = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var displayOrders = new List<int>();

        foreach (var container in view.Containers)
        {
            if (!seen.Add(container.Id))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.DuplicateContainerId,
                    container.Id,
                    $"{At(view.Grouping)}two containers share the id '{container.Id}'. Container ids "
                        + "must be unique within a grouping.",
                    view.Grouping));
            }

            displayOrders.Add(container.DisplayOrder);

            if (container.NodeIds.Count == 0)
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.EmptyContainer,
                    container.Id,
                    $"{At(view.Grouping)}container '{container.Id}' has no nodes. Remove it, or move "
                        + "nodes into it.",
                    view.Grouping));
            }

            var members = new HashSet<string>(StringComparer.Ordinal);

            foreach (var nodeId in container.NodeIds)
            {
                if (!members.Add(nodeId))
                {
                    diagnostics.Add(AnalysisDiagnostic.Error(
                        AnalysisDiagnosticCodes.DuplicateContainerMember,
                        container.Id,
                        $"{At(view.Grouping)}container '{container.Id}' lists '{nodeId}' more than "
                            + "once. The list is the reading order, so a node can only be at one "
                            + "place in it.",
                        view.Grouping));

                    continue;
                }

                if (!membership.TryGetValue(nodeId, out var owners))
                {
                    membership[nodeId] = owners = [];
                }

                owners.Add(container.Id);

                if (!nodesById.ContainsKey(nodeId))
                {
                    diagnostics.Add(AnalysisDiagnostic.Error(
                        AnalysisDiagnosticCodes.UnknownNodeReference,
                        container.Id,
                        $"{At(view.Grouping)}container '{container.Id}' lists the node '{nodeId}', "
                            + "which does not exist.",
                        view.Grouping));
                }
            }

            CheckEntryNode(view.Grouping, container, nodesById, diagnostics);
        }

        CheckDense(
            displayOrders,
            AnalysisDiagnosticCodes.DisplayOrderNotDense,
            string.Empty,
            $"{At(view.Grouping)}the container displayOrder values",
            view.Grouping,
            diagnostics);

        return membership;
    }

    /// <summary>
    /// Exactly one entry node per container, and it is the one the membership list starts with.
    /// <para>
    /// The two used to be able to disagree three ways — a container could name a node that was not
    /// in it, name one whose rank was not 1, or carry a second node marked <c>entry_point</c>. Two
    /// groupings cannot both be described by one rank or one state on a node, so the entry node is
    /// now declared once and the reading order starts with it by construction. One rule replaces
    /// three, and the reviewer still cannot be told to start in two places.
    /// </para>
    /// </summary>
    private static void CheckEntryNode(
        AnalysisGrouping grouping,
        AnalysisContainer container,
        Dictionary<string, AnalysisNode> nodesById,
        List<AnalysisDiagnostic> diagnostics)
    {
        var declared = container.EntryNodeId;

        if (string.IsNullOrWhiteSpace(declared) || !nodesById.ContainsKey(declared))
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.NoEntryNode,
                container.Id,
                $"{At(grouping)}container '{container.Id}' names no entry node that exists. Every "
                    + "container needs exactly one node a reviewer starts from.",
                grouping));

            return;
        }

        if (!container.NodeIds.Contains(declared, StringComparer.Ordinal))
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.EntryNodeNotAMember,
                container.Id,
                $"{At(grouping)}container '{container.Id}' names '{declared}' as its entry node, but "
                    + "that node is not one of its members.",
                grouping));
        }
        else if (!string.Equals(container.NodeIds[0], declared, StringComparison.Ordinal))
        {
            diagnostics.Add(AnalysisDiagnostic.Error(
                AnalysisDiagnosticCodes.EntryNodeNotFirst,
                container.Id,
                $"{At(grouping)}container '{container.Id}' starts at '{declared}', but its nodeIds "
                    + $"begin with '{container.NodeIds[0]}'. The entry node is the one the reading "
                    + "order counts from, so it comes first.",
                grouping));
        }
    }

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
        AnalysisGrouping? grouping,
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
            $"{what} are {string.Join(", ", actual.Select(static value => value.ToString(CultureInfo.InvariantCulture)))}, "
                + $"but they must be 1 to {values.Count.ToString(CultureInfo.InvariantCulture)} with no gaps and no repeats.",
            grouping));
    }

    /// <summary>Every node in exactly one container of this grouping — not none, and not two.</summary>
    private static void CheckMembership(
        AnalysisResult result,
        Dictionary<string, AnalysisNode> nodesById,
        Dictionary<string, List<string>> membership,
        AnalysisGrouping grouping,
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
                    $"{At(grouping)}node '{node.Id}' is in no container. Every node belongs to "
                        + "exactly one cluster of each grouping.",
                    grouping));
            }
            else if (owners.Count > 1)
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.NodeInManyContainers,
                    node.Id,
                    $"{At(grouping)}node '{node.Id}' is listed by "
                        + $"{owners.Count.ToString(CultureInfo.InvariantCulture)} containers "
                        + $"({string.Join(", ", owners)}). It belongs to exactly one.",
                    grouping));
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
    /// One grouping's reading order references real nodes and repeats none.
    /// <para>
    /// Not covering every node is a warning rather than an error: container order and membership
    /// order already define a complete traversal, so the application can fall back to one it derives
    /// rather than spending a repair round — and money — on a list the model merely cut short.
    /// </para>
    /// </summary>
    private static void CheckReadingOrder(
        AnalysisResult result,
        AnalysisGroupingView view,
        Dictionary<string, AnalysisNode> nodesById,
        List<AnalysisDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in view.ReadingOrder)
        {
            if (!nodesById.ContainsKey(id))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.UnknownNodeReference,
                    id,
                    $"{At(view.Grouping)}the reading order names '{id}', which is not a node in this "
                        + "result.",
                    view.Grouping));
            }
            else if (!seen.Add(id))
            {
                diagnostics.Add(AnalysisDiagnostic.Error(
                    AnalysisDiagnosticCodes.DuplicateReadingOrderEntry,
                    id,
                    $"{At(view.Grouping)}the reading order names '{id}' more than once.",
                    view.Grouping));
            }
        }

        var missing = result.Nodes.Count - seen.Count;

        if (missing > 0)
        {
            diagnostics.Add(AnalysisDiagnostic.Warning(
                AnalysisDiagnosticCodes.IncompleteReadingOrder,
                string.Empty,
                $"{At(view.Grouping)}the reading order leaves out "
                    + $"{missing.ToString(CultureInfo.InvariantCulture)} node(s); container order and "
                    + "membership order were used instead.",
                view.Grouping));
        }
    }

    /// <summary>
    /// Whether each container of one grouping is a path a reader can walk from its entry node, or a
    /// pile of files that happen to sit together.
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
    /// something the reader follows to get here. That is also why this rule is worth running twice —
    /// an edge inside a dependency-flow cluster may cross two clusters of the other grouping, and a
    /// gap the reviewer will actually meet only exists in one of the two pictures.
    /// </para>
    /// </summary>
    private static void CheckReachability(
        AnalysisResult result,
        AnalysisGroupingView view,
        List<AnalysisDiagnostic> diagnostics)
    {
        foreach (var container in view.Containers)
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
                        $"{At(view.Grouping)}nothing leads to '{nodeId}' from '{container.EntryNodeId}', "
                            + $"the start of container '{container.Id}'. A reader reaches it without "
                            + "being told why.",
                        view.Grouping));
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
    /// Fields that ran to more than twice the length they were asked for, on everything the two
    /// groupings share.
    /// <para>
    /// Warnings, always. The prompt states every budget in <see cref="AnalysisFieldBudgets"/> for the
    /// run's verbosity, and these are measured against the same set. A model that overshoots has
    /// still answered the question — the text is simply longer than the box it is read in. Making
    /// this an error would send a
    /// three-hundred-node document back to be rewritten because one sentence ran long, and that
    /// repair round costs more than the verbosity does.
    /// </para>
    /// <para>
    /// One diagnostic per offending field rather than one summary, so the subject names what to
    /// shorten. The whole result is walked: capping the count would hide exactly the case worth
    /// seeing, which is a model being verbose everywhere rather than once.
    /// </para>
    /// </summary>
    private static void CheckSharedFieldLengths(
        AnalysisResult result,
        AnalysisFieldBudgetSet budgets,
        List<AnalysisDiagnostic> diagnostics)
    {
        Report(diagnostics, string.Empty, "summary", result.Summary, budgets.OverallSummary, null);

        foreach (var risk in result.OverallRisks)
        {
            Report(diagnostics, string.Empty, "risk", risk, budgets.Risk, null);
        }

        foreach (var node in result.Nodes)
        {
            Report(diagnostics, node.Id, "title", node.Title, budgets.NodeTitle, null);
            Report(diagnostics, node.Id, "whatChanged", node.WhatChanged, budgets.NodeProse, null);
            Report(diagnostics, node.Id, "whyItChanged", node.WhyItChanged, budgets.NodeProse, null);
            Report(diagnostics, node.Id, "howItAffectsOthers", node.HowItAffectsOthers, budgets.NodeProse, null);
            Report(diagnostics, node.Id, "implementationNotes", node.ImplementationNotes, budgets.NodeProse, null);

            foreach (var risk in node.Risks)
            {
                Report(diagnostics, node.Id, "risk", risk, budgets.Risk, null);
            }
        }
    }

    /// <inheritdoc cref="CheckSharedFieldLengths"/>
    private static void CheckContainerFieldLengths(
        AnalysisGroupingView view,
        AnalysisFieldBudgetSet budgets,
        List<AnalysisDiagnostic> diagnostics)
    {
        foreach (var container in view.Containers)
        {
            Report(diagnostics, container.Id, "title", container.Title, budgets.ContainerTitle, view.Grouping);
            Report(diagnostics, container.Id, "summary", container.Summary, budgets.ContainerSummary, view.Grouping);
            Report(diagnostics, container.Id, "explanation", container.Explanation, budgets.ContainerExplanation, view.Grouping);

            foreach (var risk in container.Risks)
            {
                Report(diagnostics, container.Id, "risk", risk, budgets.Risk, view.Grouping);
            }
        }
    }

    private static void Report(
        List<AnalysisDiagnostic> diagnostics,
        string subject,
        string field,
        string? value,
        int budget,
        AnalysisGrouping? grouping)
    {
        if (!AnalysisFieldBudgets.IsOverlong(value, budget))
        {
            return;
        }

        diagnostics.Add(AnalysisDiagnostic.Warning(
            AnalysisDiagnosticCodes.VerboseField,
            subject,
            $"The {field} {(subject.Length == 0 ? "of the change as a whole" : $"of '{subject}'")} is "
                + $"{value!.Length} characters against a budget of {budget}. It will be shown truncated.",
            grouping));
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
