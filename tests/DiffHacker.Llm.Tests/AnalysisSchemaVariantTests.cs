using System.Text.Json.Nodes;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Llm;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// The analysis schema variants, checked by the validator a run actually answers through.
/// <para>
/// <c>AnalysisResponseSchemaTests</c> reads the variants as text; this asks the question a run turns
/// on. An answer without a part the run switched off has to pass, or every opt-out run fails on its
/// first answer. And an answer that writes the part anyway has to be refused, or the saving is only
/// a request the model may ignore at the reviewer's expense.
/// </para>
/// </summary>
public sealed class AnalysisSchemaVariantTests
{
    /// <summary>A complete answer for one changed file, with every optional part present.</summary>
    private const string Full =
        """
        {
          "summary": "Adds a tenant to the cache key.",
          "overallRisks": ["Existing cache entries miss after deploy."],
          "dependencyContainers": [
            { "id": "cache", "title": "Cache key", "summary": "The key.", "explanation": "Why.", "risks": [],
              "displayOrder": 1, "entryNodeId": "src/Cache.cs", "nodeIds": ["src/Cache.cs", "src/Caller.cs"] }
          ],
          "dependencyReadingOrder": ["src/Cache.cs", "src/Caller.cs"],
          "clusterContainers": [
            { "id": "cache", "title": "Cache key", "summary": "The key.", "explanation": "Why.", "risks": [],
              "displayOrder": 1, "entryNodeId": "src/Cache.cs", "nodeIds": ["src/Cache.cs", "src/Caller.cs"] }
          ],
          "clusterReadingOrder": ["src/Cache.cs", "src/Caller.cs"],
          "nodes": [
            { "id": "src/Cache.cs", "filePath": "src/Cache.cs", "symbol": "", "startLine": 0, "endLine": 0,
              "title": "Key includes tenant", "whatChanged": "A field.", "whyItChanged": "Tenancy.",
              "howItAffectsOthers": "", "implementationNotes": "", "risks": [], "importance": 4,
              "states": ["changed", "risky"] },
            { "id": "src/Caller.cs", "filePath": "src/Caller.cs", "symbol": "", "startLine": 0, "endLine": 0,
              "title": "Passes the tenant", "whatChanged": "An argument.", "whyItChanged": "The key needs it.",
              "howItAffectsOthers": "", "implementationNotes": "", "risks": [], "importance": 2,
              "states": ["changed"] }
          ],
          "edges": [
            { "sourceNodeId": "src/Cache.cs", "targetNodeId": "src/Caller.cs", "kind": "direct",
              "explanation": "The caller builds the key.", "risks": [] }
          ],
          "implementationGroups": []
        }
        """;

    [Fact]
    public void The_full_answer_matches_the_full_schema()
    {
        Validate(Full, AnalysisRunOptions.Default).ShouldBeEmpty();
    }

    [Fact]
    public void An_answer_without_risks_matches_the_schema_without_them_and_one_with_them_does_not()
    {
        var options = AnalysisRunOptions.Default with { Risks = false };

        var thin = Strip(Full, answer =>
        {
            answer.Remove("overallRisks");
            ForEach(answer, "dependencyContainers", static c => c.Remove("risks"));
            ForEach(answer, "clusterContainers", static c => c.Remove("risks"));
            ForEach(answer, "edges", static e => e.Remove("risks"));
            ForEach(answer, "nodes", static n =>
            {
                n.Remove("risks");
                n["states"] = new JsonArray("changed");
            });
        });

        Validate(thin, options).ShouldBeEmpty();

        // Writing the risks anyway is refused: the schema forbids the field, not merely omits it.
        Validate(Full, options).ShouldNotBeEmpty();

        // And so is the risky state on its own, which is a risk judgement by another name.
        var stillRisky = Strip(thin, static answer =>
            ForEach(answer, "nodes", static n => n["states"] = new JsonArray("changed", "risky")));

        Validate(stillRisky, options).ShouldNotBeEmpty();
    }

    [Fact]
    public void An_answer_without_any_prose_matches_the_schema_without_it()
    {
        var options = AnalysisRunOptions.Default with
        {
            NodeExplanations = false,
            EdgeExplanations = false,
            ContainerExplanations = false,
        };

        var thin = Strip(Full, static answer =>
        {
            ForEach(answer, "nodes", static n =>
            {
                foreach (var field in new[] { "whatChanged", "whyItChanged", "howItAffectsOthers", "implementationNotes" })
                {
                    n.Remove(field);
                }
            });
            ForEach(answer, "edges", static e => e.Remove("explanation"));
            ForEach(answer, "dependencyContainers", static c => { c.Remove("summary"); c.Remove("explanation"); });
            ForEach(answer, "clusterContainers", static c => { c.Remove("summary"); c.Remove("explanation"); });
        });

        Validate(thin, options).ShouldBeEmpty();
        Validate(Full, options).ShouldNotBeEmpty();

        // The overall summary is never optional.
        Validate(Strip(thin, static answer => answer.Remove("summary")), options).ShouldNotBeEmpty();
    }

    private static IReadOnlyList<string> Validate(string answer, AnalysisRunOptions options) =>
        StructuredOutput.Validate(answer, new LlmResponseFormat
        {
            SchemaName = AnalysisPrompt.SchemaName,
            SchemaJson = AnalysisResponseSchema.For(options),
        });

    private static string Strip(string answer, Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(answer)!.AsObject();
        edit(root);
        return root.ToJsonString();
    }

    private static void ForEach(JsonObject answer, string list, Action<JsonObject> edit)
    {
        foreach (var item in answer[list]!.AsArray())
        {
            edit(item!.AsObject());
        }
    }
}
