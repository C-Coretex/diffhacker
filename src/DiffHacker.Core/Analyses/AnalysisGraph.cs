namespace DiffHacker.Core.Analyses;

/// <summary>
/// The model's nodes and edges as a graph, with the few things that have to be walked rather than
/// read off: cycles, fan-in, fan-out and the longest reading chain.
/// <para>
/// Built once and used twice — the validator reports the cycles, the statistics count the rest —
/// because finding the strongly connected components twice over a fifteen-hundred-node change
/// would be the same walk done twice for two callers that disagree about nothing.
/// </para>
/// <para>
/// Edges whose ends do not exist are dropped here. They are a validation error reported by name
/// elsewhere, and carrying them into the walk would only turn one clear diagnostic into a crash.
/// </para>
/// </summary>
public sealed class AnalysisGraph
{
    private readonly string[] _ids;
    private readonly int[][] _outgoing;
    private readonly int[] _fanIn;
    private readonly int[] _fanOut;

    private AnalysisGraph(string[] ids, int[][] outgoing, int[] fanIn, int[] fanOut)
    {
        _ids = ids;
        _outgoing = outgoing;
        _fanIn = fanIn;
        _fanOut = fanOut;

        var components = StronglyConnectedComponents();
        Cycles = FindCycles(components);
        LongestChain = MeasureLongestChain(components);
    }

    /// <summary>
    /// Every set of nodes that can reach each other, each sorted by id and the sets sorted between
    /// themselves, so two runs over the same graph report the same cycles in the same order.
    /// Self-edges are not here: a node pointing at itself is reported on its own.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Cycles { get; }

    /// <summary>
    /// Nodes on the longest reading path. Measured over the graph's strongly connected components
    /// rather than the raw graph, so a cycle counts as the one thing it is instead of making the
    /// answer unbounded.
    /// </summary>
    public int LongestChain { get; }

    public int HighestFanIn => _fanIn.Length == 0 ? 0 : _fanIn.Max();

    public int HighestFanOut => _fanOut.Length == 0 ? 0 : _fanOut.Max();

    /// <summary>
    /// The node the most edges arrive at, or null when there are no edges at all. Ties break on
    /// the id so that the same graph always names the same node.
    /// </summary>
    public string? HighestFanInNodeId => Busiest(_fanIn);

    /// <inheritdoc cref="HighestFanInNodeId"/>
    public string? HighestFanOutNodeId => Busiest(_fanOut);

    public static AnalysisGraph Build(IReadOnlyList<AnalysisNode> nodes, IReadOnlyList<AnalysisEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var ids = new List<string>(nodes.Count);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (node.Id is { Length: > 0 } id && index.TryAdd(id, ids.Count))
            {
                ids.Add(id);
            }
        }

        var outgoing = new List<int>[ids.Count];
        var fanIn = new int[ids.Count];
        var fanOut = new int[ids.Count];

        for (var i = 0; i < outgoing.Length; i++)
        {
            outgoing[i] = [];
        }

        foreach (var edge in edges)
        {
            if (!index.TryGetValue(edge.SourceNodeId ?? string.Empty, out var source) ||
                !index.TryGetValue(edge.TargetNodeId ?? string.Empty, out var target) ||
                source == target)
            {
                continue;
            }

            outgoing[source].Add(target);
            fanOut[source]++;
            fanIn[target]++;
        }

        return new AnalysisGraph(
            [.. ids],
            [.. outgoing.Select(static targets => targets.ToArray())],
            fanIn,
            fanOut);
    }

    private string? Busiest(int[] counts)
    {
        var best = -1;

        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            if (best < 0 ||
                counts[i] > counts[best] ||
                (counts[i] == counts[best] && string.CompareOrdinal(_ids[i], _ids[best]) < 0))
            {
                best = i;
            }
        }

        return best < 0 ? null : _ids[best];
    }

    /// <summary>
    /// Tarjan's algorithm, written with an explicit stack rather than recursion: a fifteen-hundred
    /// file change is an allowed size (§0.2.10), and a graph that deep is not a thing to hand to
    /// the call stack.
    /// </summary>
    private int[] StronglyConnectedComponents()
    {
        var count = _ids.Length;
        var component = new int[count];
        var discovery = new int[count];
        var low = new int[count];
        var onStack = new bool[count];
        var pending = new Stack<int>();
        var frames = new Stack<(int Node, int Edge)>();

        Array.Fill(component, -1);
        Array.Fill(discovery, -1);

        var nextIndex = 0;
        var nextComponent = 0;

        for (var root = 0; root < count; root++)
        {
            if (discovery[root] >= 0)
            {
                continue;
            }

            frames.Push((root, 0));
            discovery[root] = low[root] = nextIndex++;
            pending.Push(root);
            onStack[root] = true;

            while (frames.Count > 0)
            {
                var (node, edge) = frames.Pop();

                if (edge < _outgoing[node].Length)
                {
                    frames.Push((node, edge + 1));
                    var next = _outgoing[node][edge];

                    if (discovery[next] < 0)
                    {
                        discovery[next] = low[next] = nextIndex++;
                        pending.Push(next);
                        onStack[next] = true;
                        frames.Push((next, 0));
                    }
                    else if (onStack[next])
                    {
                        low[node] = Math.Min(low[node], discovery[next]);
                    }

                    continue;
                }

                if (low[node] == discovery[node])
                {
                    int member;

                    do
                    {
                        member = pending.Pop();
                        onStack[member] = false;
                        component[member] = nextComponent;
                    }
                    while (member != node);

                    nextComponent++;
                }

                if (frames.Count > 0)
                {
                    var parent = frames.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }
            }
        }

        return component;
    }

    private IReadOnlyList<IReadOnlyList<string>> FindCycles(int[] component)
    {
        var members = new Dictionary<int, List<string>>();

        for (var i = 0; i < component.Length; i++)
        {
            if (!members.TryGetValue(component[i], out var group))
            {
                members[component[i]] = group = [];
            }

            group.Add(_ids[i]);
        }

        return
        [
            .. members.Values
                .Where(static group => group.Count > 1)
                .Select(static group => (IReadOnlyList<string>)[.. group.Order(StringComparer.Ordinal)])
                .OrderBy(static group => group[0], StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Longest path through the condensation, by definition acyclic, so a plain longest-path walk
    /// over it terminates. Measured in nodes rather than edges: a lone node is a chain of one.
    /// </summary>
    private int MeasureLongestChain(int[] component)
    {
        if (_ids.Length == 0)
        {
            return 0;
        }

        var componentCount = component.Max() + 1;
        var size = new int[componentCount];
        var outgoing = new HashSet<int>[componentCount];

        for (var i = 0; i < componentCount; i++)
        {
            outgoing[i] = [];
        }

        for (var i = 0; i < component.Length; i++)
        {
            size[component[i]]++;

            foreach (var target in _outgoing[i])
            {
                if (component[i] != component[target])
                {
                    outgoing[component[i]].Add(component[target]);
                }
            }
        }

        // longest[c] is the longest chain starting at component c. Tarjan numbers a component
        // only once everything it can reach is numbered, so every target of c has an index below
        // c's and is already final by the time c is reached — one upward pass, no sorting.
        var longest = new int[componentCount];
        var best = 0;

        for (var current = 0; current < componentCount; current++)
        {
            var onward = 0;

            foreach (var target in outgoing[current])
            {
                onward = Math.Max(onward, longest[target]);
            }

            longest[current] = size[current] + onward;
            best = Math.Max(best, longest[current]);
        }

        return best;
    }
}
