using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiffHacker.Contracts;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// The schema the model answers in, in the variants a run can ask for.
/// <para>
/// A reviewer who does not want a part of the analysis should not pay for it, and the way to make
/// that real is to take it out of the request rather than to ask for it and discard it. It is worth
/// more than it sounds: the response schema is ~15,700 of the ~28,500 characters in the fixed
/// preamble, and <c>StructuredOutput.PromptSuffix</c> appends it to the system prompt as well as
/// sending it as the response format — so every character of it is paid for twice on each of up to
/// three hundred turns. The second grouping, implementation groups, risks and the node, edge and
/// cluster explanations are each optional on those terms; verbosity changes the prompt, never this.
/// </para>
/// <para>
/// Derived from the one schema in <c>/schema</c> rather than kept as further files. Sixty-four files
/// would be sixty-four things to bump, keep in step, and let quietly stop matching the contract the
/// validator enforces.
/// </para>
/// <para>
/// A part that is removed is removed from the descriptions too. A description that says "no risks
/// here" to a model with nowhere to put a risk is an invitation, and it is paid for twice a turn.
/// <see cref="Rewrite"/> edits the few sentences that mention a removed part; the tests check the
/// result mentions it nowhere, so a description added later that names one fails loudly.
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

    private const string ContainerDefinition = "analysisContainer";

    private const string NodeDefinition = "analysisNode";

    private const string EdgeDefinition = "analysisEdge";

    /// <summary>The four prose fields of a node, which exist only when node explanations were asked for.</summary>
    private static readonly string[] NodeProseProperties =
        ["whatChanged", "whyItChanged", "howItAffectsOthers", "implementationNotes"];

    /// <summary>The prose of a container, which exists only when cluster explanations were asked for.</summary>
    private static readonly string[] ContainerProseProperties = ["summary", "explanation"];

    /// <summary>The node state that is a risk judgement, removed from the enum with the risks.</summary>
    private const string RiskyState = "risky";

    /// <summary>Where the remaining descriptions mention risk, and what they say instead.</summary>
    private static readonly (string Find, string Replace)[] RiskMentions =
    [
        (" and what is risky.", "."),
        (" No risks here.", string.Empty),
        (": an added file that is also risky carries two.", "."),
        ("; risky is your own judgement.", "."),
    ];

    /// <summary>Where a surviving description names a node prose field.</summary>
    private static readonly (string Find, string Replace)[] NodeProseMentions =
    [
        (" Never fold a risk into whatChanged, whyItChanged or implementationNotes - risks are read as their own column.", string.Empty),
    ];

    private static readonly ConcurrentDictionary<SchemaParts, string> Cache = new();

    /// <summary>
    /// The schema text for a run: every part it asked for, and none it did not.
    /// </summary>
    public static string For(AnalysisRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Cache.GetOrAdd(SchemaParts.Of(options), static parts => Build(parts));
    }

    private static string Full() => ContractSchemas.Get(AnalysisPrompt.SchemaKey);

    private static string Build(SchemaParts parts)
    {
        if (parts.IsEverything)
        {
            // Byte for byte the file, so the full request is provably the contract as written.
            return Full();
        }

        var root = JsonNode.Parse(Full())?.AsObject()
            ?? throw new InvalidOperationException(
                "The analysis result schema is not a JSON object. That is a build-time mistake, not "
                    + "something a run can recover from.");

        var definitions = root["$defs"] as JsonObject;

        if (!parts.ChangeClusters)
        {
            Remove(root, ClusterProperties);
        }

        if (!parts.ImplementationGroups)
        {
            Remove(root, ImplementationGroupProperties);

            // The definition goes too: nothing refers to it any more, and it would otherwise be paid
            // for twice a turn to describe a field the model is not allowed to write.
            definitions?.Remove(ImplementationGroupDefinition);
        }

        if (!parts.Risks)
        {
            Remove(root, ["overallRisks"]);

            foreach (var name in new[] { ContainerDefinition, NodeDefinition, EdgeDefinition })
            {
                if (definitions?[name] is JsonObject definition)
                {
                    Remove(definition, ["risks"]);
                }
            }

            if (definitions?[NodeDefinition]?["properties"]?["states"]?["items"]?["enum"] is JsonArray states)
            {
                foreach (var risky in states.Where(static s => s?.GetValue<string>() == RiskyState).ToArray())
                {
                    states.Remove(risky);
                }
            }

            Rewrite(root, RiskMentions);
        }

        if (!parts.NodeExplanations && definitions?[NodeDefinition] is JsonObject node)
        {
            Remove(node, NodeProseProperties);
            Rewrite(root, NodeProseMentions);
        }

        if (!parts.EdgeExplanations && definitions?[EdgeDefinition] is JsonObject edge)
        {
            Remove(edge, ["explanation"]);
        }

        if (!parts.ContainerExplanations && definitions?[ContainerDefinition] is JsonObject container)
        {
            Remove(container, ContainerProseProperties);
        }

        return root.ToJsonString(Output);
    }

    /// <summary>
    /// How a variant is written back out: as close to the file it was cut from as the writer allows,
    /// because anything the writer adds is paid for twice a turn and can outweigh what was removed.
    /// Relaxed escaping, because the default encoder writes every apostrophe and dash in the
    /// descriptions as a six-character <c>\uXXXX</c> escape; the text is never embedded in HTML, which
    /// is the only thing the stricter escaping is for. And <c>\n</c>, because the default is the
    /// platform's newline — a character more per line on Windows, and a request that differs by OS.
    /// </summary>
    private static readonly JsonSerializerOptions Output = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Removes <paramref name="names"/> from an object schema's <c>properties</c> and <c>required</c>
    /// — the root's or a definition's. <c>additionalProperties</c> is already false throughout, so a
    /// model that answers with them anyway is rejected by the ordinary schema check rather than by
    /// anything special here.
    /// </summary>
    private static void Remove(JsonObject schema, string[] names)
    {
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var name in names)
            {
                properties.Remove(name);
            }
        }

        if (schema["required"] is JsonArray required)
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

    /// <summary>
    /// Applies <paramref name="edits"/> to every <c>description</c> in the tree. A phrase that is not
    /// found is left alone rather than thrown over: the tests read the result for the part's name,
    /// which catches a description that has drifted away from these edits.
    /// </summary>
    private static void Rewrite(JsonNode? node, (string Find, string Replace)[] edits)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToArray())
                {
                    if (key == "description" && value is JsonValue text && text.TryGetValue<string>(out var description))
                    {
                        foreach (var (find, replace) in edits)
                        {
                            description = description.Replace(find, replace, StringComparison.Ordinal);
                        }

                        obj[key] = description;
                    }
                    else
                    {
                        Rewrite(value, edits);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    Rewrite(item, edits);
                }

                break;
        }
    }

    /// <summary>
    /// The options that shape the schema. Verbosity is not one of them, so two runs that differ only
    /// in how much they write share one cached schema.
    /// </summary>
    private readonly record struct SchemaParts(
        bool ChangeClusters,
        bool ImplementationGroups,
        bool Risks,
        bool NodeExplanations,
        bool EdgeExplanations,
        bool ContainerExplanations)
    {
        public bool IsEverything =>
            ChangeClusters && ImplementationGroups && Risks && NodeExplanations && EdgeExplanations
            && ContainerExplanations;

        public static SchemaParts Of(AnalysisRunOptions options) => new(
            options.ChangeClusters,
            options.ImplementationGroups,
            options.Risks,
            options.NodeExplanations,
            options.EdgeExplanations,
            options.ContainerExplanations);
    }
}
