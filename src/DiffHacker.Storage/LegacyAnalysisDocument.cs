using System.Text.Json;
using System.Text.Json.Nodes;
using DiffHacker.Core.Analyses;

namespace DiffHacker.Storage;

/// <summary>
/// Reads a stored analysis document written by an older build.
/// <para>
/// Iteration 11 renamed the fields that carry the grouping — one <c>containers</c> and one
/// <c>readingOrder</c> became a pair per grouping — and moved reading order off the node, because a
/// single <c>rank</c> and a single <c>entry_point</c> state cannot describe two groupings at once.
/// A document written before that has none of the new names, and would deserialise into a result
/// with no clusters at all: an analysis someone paid for, opening as an empty diagram.
/// </para>
/// <para>
/// So it is upgraded on the way out instead. The old grouping becomes dependency flow, each
/// container's members are sorted by the rank they carried, the <c>entry_point</c> state is dropped
/// now that the container declares the entry node, and there is no change-clusters grouping —
/// nothing here invents one. <c>AnalysisView.availableGroupings</c> then reports the one grouping the
/// analysis has, and the control offering the other is disabled with a reason rather than failing.
/// </para>
/// <para>
/// Detected by shape rather than by the row's schema version. The version says what the document was
/// written against, but a document is upgradeable exactly when it has the old field and not the new
/// one, and reading that off the JSON cannot disagree with itself.
/// </para>
/// </summary>
internal static class LegacyAnalysisDocument
{
    public static AnalysisResult Read(string documentJson)
    {
        var upgraded = Upgrade(documentJson) ?? documentJson;

        return JsonSerializer.Deserialize<AnalysisResult>(upgraded, StorageJson.Options)!;
    }

    /// <summary>The rewritten JSON, or null when the document already speaks the current contract.</summary>
    private static string? Upgrade(string documentJson)
    {
        JsonObject? root;

        try
        {
            root = JsonNode.Parse(documentJson)?.AsObject();
        }
        catch (JsonException)
        {
            // Left to the ordinary deserialisation to fail on, so there is one error path for a
            // document this build cannot read rather than two that report it differently.
            return null;
        }

        if (root is null ||
            root["dependencyContainers"] is not null ||
            root["containers"] is not JsonArray containers)
        {
            return null;
        }

        var ranks = RanksByNodeId(root);

        foreach (var container in containers.OfType<JsonObject>())
        {
            SortMembers(container, ranks);
        }

        root["dependencyContainers"] = containers.DeepClone();
        root.Remove("containers");

        root["dependencyReadingOrder"] = root["readingOrder"]?.DeepClone() ?? new JsonArray();
        root.Remove("readingOrder");

        root["clusterContainers"] = new JsonArray();
        root["clusterReadingOrder"] = new JsonArray();

        foreach (var node in (root["nodes"] as JsonArray ?? []).OfType<JsonObject>())
        {
            node.Remove("rank");
            DropEntryPointState(node);
        }

        return root.ToJsonString();
    }

    private static Dictionary<string, int> RanksByNodeId(JsonObject root)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in (root["nodes"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (node["id"]?.GetValue<string>() is { } id &&
                node["rank"] is { } rank &&
                rank.GetValueKind() is JsonValueKind.Number)
            {
                ranks[id] = rank.GetValue<int>();
            }
        }

        return ranks;
    }

    /// <summary>
    /// The old order, made explicit. Members with no rank keep their listed position relative to each
    /// other and follow the ranked ones — the same tolerance <see cref="AnalysisReadingOrder"/> shows,
    /// and for the same reason: §0.2.5 means a node is never dropped to tidy up an ordering.
    /// </summary>
    private static void SortMembers(JsonObject container, Dictionary<string, int> ranks)
    {
        if (container["nodeIds"] is not JsonArray nodeIds)
        {
            return;
        }

        var ordered = nodeIds
            .Select(static entry => entry?.GetValue<string>())
            .Where(static id => id is not null)
            .Select((id, position) => (Id: id!, Position: position))
            .OrderBy(member => ranks.TryGetValue(member.Id, out var rank) ? rank : int.MaxValue)
            .ThenBy(static member => member.Position)
            .Select(static member => member.Id)
            .ToArray();

        nodeIds.Clear();

        foreach (var id in ordered)
        {
            nodeIds.Add(id);
        }
    }

    private static void DropEntryPointState(JsonObject node)
    {
        if (node["states"] is not JsonArray states)
        {
            return;
        }

        var kept = states
            .Select(static entry => entry?.GetValue<string>())
            .Where(static state => state is not null && !string.Equals(state, "entry_point", StringComparison.Ordinal))
            .ToArray();

        states.Clear();

        foreach (var state in kept)
        {
            states.Add(state);
        }
    }
}
