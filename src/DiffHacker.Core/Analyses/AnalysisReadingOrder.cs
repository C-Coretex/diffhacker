namespace DiffHacker.Core.Analyses;

/// <summary>
/// The traversal a reviewer is offered across the whole change.
/// <para>
/// The model writes one, and when it covers every node that is the answer — it knows the change
/// and the application does not. When it falls short, the fallback is not a guess either: container
/// display order and node rank are also the model's own layout intent, so the derived order is
/// still its judgement, just read out of a different field. That is why an incomplete reading order
/// is a warning rather than a repair round — there is nothing to ask the model that it has not
/// already said.
/// </para>
/// </summary>
public static class AnalysisReadingOrder
{
    public static IReadOnlyList<string> Resolve(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var known = new HashSet<string>(
            result.Nodes.Select(static node => node.Id),
            StringComparer.Ordinal);

        var stated = new List<string>(result.ReadingOrder.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in result.ReadingOrder)
        {
            if (known.Contains(id) && seen.Add(id))
            {
                stated.Add(id);
            }
        }

        return stated.Count == known.Count ? stated : Derive(result);
    }

    /// <summary>Containers in display order, and within each, nodes by rank.</summary>
    private static List<string> Derive(AnalysisResult result)
    {
        var ranks = result.Nodes.ToDictionary(
            static node => node.Id,
            static node => node.Rank,
            StringComparer.Ordinal);

        var order = new List<string>(result.Nodes.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var container in result.Containers.OrderBy(static c => c.DisplayOrder))
        {
            var members = container.NodeIds
                .Where(id => ranks.ContainsKey(id) && !placed.Contains(id))
                .OrderBy(id => ranks[id])
                .ThenBy(static id => id, StringComparer.Ordinal);

            foreach (var id in members)
            {
                order.Add(id);
                placed.Add(id);
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
