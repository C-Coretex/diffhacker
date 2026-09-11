using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiffHacker.Contracts;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// The schema the model answers in, in the variants a run can ask for.
/// <para>
/// A reviewer who never switches grouping should not pay for the second one, and the way to make
/// that real is to take it out of the request rather than to ask for it and discard it. It is worth
/// more than it sounds: the response schema is ~15,700 of the ~28,500 characters in the fixed
/// preamble, and <c>StructuredOutput.PromptSuffix</c> appends it to the system prompt as well as
/// sending it as the response format — so every character of it is paid for twice on each of up to
/// three hundred turns. Implementation groups are optional on the same terms.
/// </para>
/// <para>
/// Derived from the one schema in <c>/schema</c> rather than kept as further files. Four files would
/// be four things to bump, four things to keep in step, and a way for a variant to quietly stop
/// matching the contract the validator enforces.
/// </para>
/// </summary>
public static class AnalysisResponseSchema
{
    /// <summary>The properties that exist only when the second grouping was asked for.</summary>
    private static readonly string[] ClusterProperties = ["clusterContainers", "clusterReadingOrder"];

    /// <summary>The property that exists only when implementation groups were asked for.</summary>
    private static readonly string[] ImplementationGroupProperties = ["implementationGroups"];

    /// <summary>The definition only <see cref="ImplementationGroupProperties"/> refers to.</summary>
    private const string ImplementationGroupDefinition = "analysisImplementationGroup";

    private static readonly ConcurrentDictionary<(bool Clusters, bool Groups), string> Cache = new();

    /// <summary>
    /// The schema text for a run, with the change-clusters grouping and the implementation groups
    /// each in it or absent entirely.
    /// </summary>
    public static string For(bool changeClusters, bool implementationGroups = true) =>
        Cache.GetOrAdd((changeClusters, implementationGroups), static wanted => Build(wanted.Clusters, wanted.Groups));

    private static string Full() => ContractSchemas.Get(AnalysisPrompt.SchemaKey);

    private static string Build(bool changeClusters, bool implementationGroups)
    {
        if (changeClusters && implementationGroups)
        {
            // Byte for byte the file, so the full request is provably the contract as written.
            return Full();
        }

        var root = JsonNode.Parse(Full())?.AsObject()
            ?? throw new InvalidOperationException(
                "The analysis result schema is not a JSON object. That is a build-time mistake, not "
                    + "something a run can recover from.");

        if (!changeClusters)
        {
            Remove(root, ClusterProperties);
        }

        if (!implementationGroups)
        {
            Remove(root, ImplementationGroupProperties);

            // The definition goes too: nothing refers to it any more, and it would otherwise be paid
            // for twice a turn to describe a field the model is not allowed to write.
            (root["$defs"] as JsonObject)?.Remove(ImplementationGroupDefinition);
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Removes <paramref name="names"/> from <c>properties</c> and from <c>required</c>.
    /// <c>additionalProperties</c> is already false, so a model that answers with them anyway is
    /// rejected by the ordinary schema check rather than by anything special here.
    /// </summary>
    private static void Remove(JsonObject root, string[] names)
    {
        if (root["properties"] is JsonObject properties)
        {
            foreach (var name in names)
            {
                properties.Remove(name);
            }
        }

        if (root["required"] is JsonArray required)
        {
            var keep = required
                .Where(entry => !names.Contains(entry?.GetValue<string>(), StringComparer.Ordinal))
                .Select(static entry => entry?.GetValue<string>())
                .ToArray();

            required.Clear();

            foreach (var name in keep)
            {
                required.Add(name);
            }
        }
    }
}
