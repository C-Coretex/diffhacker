namespace DiffHacker.Core.Analyses;

/// <summary>
/// The traversal a reviewer is offered across the whole change, in one grouping.
/// <para>
/// The model writes one per grouping, and when it covers every node that is the answer — it knows
/// the change and the application does not. When it falls short, the fallback is not a guess either:
/// container display order and the order each container lists its members in are also the model's
/// own layout intent, so the derived order is still its judgement, just read out of a different
/// field. That is why an incomplete reading order is a warning rather than a repair round — there is
/// nothing to ask the model that it has not already said.
/// </para>
/// </summary>
public static class AnalysisReadingOrder
{
    public static IReadOnlyList<string> Resolve(AnalysisResult result, AnalysisGrouping grouping)
    {
        ArgumentNullException.ThrowIfNull(result);

        var view = result.For(grouping);

        var known = new HashSet<string>(
            result.Nodes.Select(static node => node.Id),
            StringComparer.Ordinal);

        var stated = new List<string>(view.ReadingOrder.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in view.ReadingOrder)
        {
            if (known.Contains(id) && seen.Add(id))
            {
                stated.Add(id);
            }
        }

        return stated.Count == known.Count ? stated : Derive(result, view);
    }

    /// <summary>Containers in display order, and within each, nodes in the order it lists them.</summary>
    private static List<string> Derive(AnalysisResult result, AnalysisGroupingView view)
    {
        var known = new HashSet<string>(
            result.Nodes.Select(static node => node.Id),
            StringComparer.Ordinal);

        var order = new List<string>(result.Nodes.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var container in view.Containers.OrderBy(static c => c.DisplayOrder))
        {
            foreach (var id in container.NodeIds)
            {
                if (known.Contains(id) && placed.Add(id))
                {
                    order.Add(id);
                }
            }
        }

        // A node no container claimed cannot reach here — validation refuses that result — but a
        // traversal that silently dropped one would be the exact failure §0.2.5 exists to prevent,
        // so the remainder is appended rather than assumed away.
        foreach (var node in result.Nodes)
        {
            if (placed.Add(node.Id))
            {
                order.Add(node.Id);
            }
        }

        return order;
    }
}
