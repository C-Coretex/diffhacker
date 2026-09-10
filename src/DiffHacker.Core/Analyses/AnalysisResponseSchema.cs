using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiffHacker.Contracts;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// The schema the model answers in, in the one or two variants a run can ask for.
/// <para>
/// A reviewer who never switches grouping should not pay for the second one, and the way to make
/// that real is to take it out of the request rather than to ask for it and discard it. It is worth
/// more than it sounds: the response schema is ~15,700 of the ~28,500 characters in the fixed
/// preamble, and <c>StructuredOutput.PromptSuffix</c> appends it to the system prompt as well as
/// sending it as the response format — so every character of it is paid for twice on each of up to
/// three hundred turns.
/// </para>
/// <para>
/// Derived from the one schema in <c>/schema</c> rather than kept as a second file. Two files would
/// be two things to bump, two things to keep in step, and a way for the variant to quietly stop
/// matching the contract the validator enforces.
/// </para>
/// </summary>
public static class AnalysisResponseSchema
{
    /// <summary>The properties that exist only when the second grouping was asked for.</summary>
    private static readonly string[] ClusterProperties = ["clusterContainers", "clusterReadingOrder"];

    private static readonly ConcurrentDictionary<bool, string> Cache = new();

    /// <summary>
    /// The schema text for a run, with the change-clusters grouping in it or absent entirely.
    /// </summary>
    public static string For(bool changeClusters) =>
        Cache.GetOrAdd(changeClusters, static wanted => wanted ? Full() : WithoutClusters());

    private static string Full() => ContractSchemas.Get(AnalysisPrompt.SchemaKey);

    /// <summary>
    /// The same schema with the two cluster properties removed from <c>properties</c> and from
    /// <c>required</c>. <c>additionalProperties</c> is already false, so a model that answers with
    /// them anyway is rejected by the ordinary schema check rather than by anything special here.
    /// </summary>
    private static string WithoutClusters()
    {
        var root = JsonNode.Parse(Full())?.AsObject()
            ?? throw new InvalidOperationException(
                "The analysis result schema is not a JSON object. That is a build-time mistake, not "
                    + "something a run can recover from.");

        if (root["properties"] is JsonObject properties)
        {
            foreach (var name in ClusterProperties)
            {
                properties.Remove(name);
            }
        }

        if (root["required"] is JsonArray required)
        {
            var keep = required
                .Where(entry => !ClusterProperties.Contains(entry?.GetValue<string>(), StringComparer.Ordinal))
                .Select(static entry => entry?.GetValue<string>())
                .ToArray();

            required.Clear();

            foreach (var name in keep)
            {
                required.Add(name);
            }
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
