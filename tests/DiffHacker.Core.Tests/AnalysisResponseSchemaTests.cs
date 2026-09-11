using System.Text.Json;
using System.Text.Json.Nodes;
using DiffHacker.Contracts;
using DiffHacker.Core.Analyses;

namespace DiffHacker.Core.Tests;

/// <summary>
/// The shapes a run can ask the model to answer in.
/// <para>
/// The variant matters more than its size suggests. The response schema is over half the fixed
/// preamble of every request, and <c>StructuredOutput</c> sends it twice — as the provider's
/// response format and appended to the system prompt — on each of up to three hundred turns. So
/// "the reviewer does not want this part" has to mean the schema does not mention it: not its
/// fields, and not a description elsewhere that talks about it.
/// </para>
/// </summary>
public sealed class AnalysisResponseSchemaTests
{
    /// <summary>What <c>AnalysisRunner</c> reads an answer back with.</summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Asking_for_every_part_sends_the_contract_exactly_as_it_is_written()
    {
        // Not a copy, not a rewrite: the schema in /schema is the contract the validator enforces,
        // and the default path must not be able to drift from it.
        Schema().ShouldBe(ContractSchemas.Get(AnalysisPrompt.SchemaKey));
    }

    [Theory]
    [InlineData(AnalysisVerbosity.Brief)]
    [InlineData(AnalysisVerbosity.Medium)]
    [InlineData(AnalysisVerbosity.Detailed)]
    public void Verbosity_never_changes_the_schema(AnalysisVerbosity verbosity)
    {
        // Lengths are asked for in the prompt, never enforced in the schema — see AnalysisFieldBudgets.
        AnalysisResponseSchema.For(AnalysisRunOptions.Default with { Verbosity = verbosity })
            .ShouldBe(ContractSchemas.Get(AnalysisPrompt.SchemaKey));
    }

    [Fact]
    public void Dropping_the_second_grouping_removes_exactly_two_properties()
    {
        var full = Properties(Schema());
        var reduced = Properties(Schema(changeClusters: false));

        full.Except(reduced, StringComparer.Ordinal)
            .ShouldBe(["clusterContainers", "clusterReadingOrder"], ignoreOrder: true);

        reduced.Except(full, StringComparer.Ordinal).ShouldBeEmpty();
    }

    [Fact]
    public void The_removed_properties_are_taken_out_of_required_as_well_as_out_of_properties()
    {
        // A property listed as required but not defined is a schema a provider rejects, and it would
        // fail on the first turn of every opt-out run.
        var required = Required(Schema(changeClusters: false));

        required.ShouldNotContain("clusterContainers");
        required.ShouldNotContain("clusterReadingOrder");
        required.ShouldContain("dependencyContainers");
        required.ShouldContain("nodes");

        // Every remaining required property is still defined.
        var properties = Properties(Schema(changeClusters: false));

        foreach (var name in required)
        {
            properties.ShouldContain(name);
        }
    }

    [Fact]
    public void The_container_definition_survives_because_the_first_grouping_still_needs_it()
    {
        Definitions(Schema(changeClusters: false)).ShouldContain("analysisContainer");
    }

    [Fact]
    public void The_smaller_schema_is_measurably_smaller()
    {
        // The whole point, stated as a number. It is sent twice per request, so the saving is twice
        // this on every turn.
        Schema(changeClusters: false).Length.ShouldBeLessThan(Schema().Length);
    }

    [Fact]
    public void Every_variant_is_still_valid_JSON_and_requires_only_what_it_defines()
    {
        foreach (var options in EveryVariant())
        {
            var schema = AnalysisResponseSchema.For(options);

            Should.NotThrow(() => JsonDocument.Parse(schema));

            var root = JsonNode.Parse(schema)!.AsObject();
            RequiredAreDefined(root);

            foreach (var (_, definition) in root["$defs"]!.AsObject())
            {
                RequiredAreDefined(definition!.AsObject());
            }
        }
    }

    [Fact]
    public void Dropping_implementation_groups_removes_exactly_that_property_and_its_definition()
    {
        var full = Schema();
        var reduced = Schema(implementationGroups: false);

        Properties(full).Except(Properties(reduced), StringComparer.Ordinal)
            .ShouldBe(["implementationGroups"]);

        Required(reduced).ShouldNotContain("implementationGroups");
        Required(reduced).ShouldContain("clusterContainers");

        // The definition only that property refers to goes too — it would otherwise be paid for
        // twice a turn to describe a field the model may not write.
        Definitions(full).Except(Definitions(reduced), StringComparer.Ordinal)
            .ShouldBe(["analysisImplementationGroup"]);

        reduced.Length.ShouldBeLessThan(full.Length);
    }

    [Fact]
    public void The_two_grouping_opt_outs_compose()
    {
        var neither = Schema(changeClusters: false, implementationGroups: false);

        Properties(Schema()).Except(Properties(neither), StringComparer.Ordinal)
            .ShouldBe(["clusterContainers", "clusterReadingOrder", "implementationGroups"], ignoreOrder: true);

        foreach (var name in Required(neither))
        {
            Properties(neither).ShouldContain(name);
        }
    }

    [Fact]
    public void Without_risks_the_schema_never_mentions_a_risk_anywhere()
    {
        // Not only the fields: a description saying "no risks here" to a model that has nowhere to
        // put one is an invitation, paid for twice a turn. A description added later that talks
        // about risk fails here rather than quietly reintroducing the work.
        var schema = Schema(risks: false);

        schema.ShouldNotContain("risk", Case.Insensitive);

        Properties(schema).ShouldNotContain("overallRisks");

        foreach (var name in new[] { "analysisContainer", "analysisNode", "analysisEdge" })
        {
            DefinitionProperties(schema, name).ShouldNotContain("risks");
        }

        // The risky state is a risk judgement, so it goes from the enum too, and the others stay.
        StateValues(schema).ShouldBe(["changed", "added", "deleted", "unchanged_relevant"]);

        schema.Length.ShouldBeLessThan(Schema().Length);
    }

    [Fact]
    public void Without_node_explanations_a_node_is_its_title_place_and_importance()
    {
        var schema = Schema(nodeExplanations: false);
        var prose = new[] { "whatChanged", "whyItChanged", "howItAffectsOthers", "implementationNotes" };

        foreach (var field in prose)
        {
            // Named nowhere — including the risks description, which used to point at them.
            schema.ShouldNotContain(field);
        }

        DefinitionProperties(schema, "analysisNode").ShouldContain("title");
        DefinitionProperties(schema, "analysisNode").ShouldContain("importance");
        DefinitionProperties(schema, "analysisNode").ShouldContain("risks");

        schema.Length.ShouldBeLessThan(Schema().Length);
    }

    [Fact]
    public void Without_edge_explanations_an_edge_is_its_ends_and_its_kind()
    {
        var schema = Schema(edgeExplanations: false);

        DefinitionProperties(schema, "analysisEdge")
            .ShouldBe(["sourceNodeId", "targetNodeId", "kind", "risks"], ignoreOrder: true);

        // Cluster explanations are a different part and are still there.
        DefinitionProperties(schema, "analysisContainer").ShouldContain("explanation");
    }

    [Fact]
    public void Without_cluster_explanations_a_container_keeps_its_title_but_loses_its_prose()
    {
        var schema = Schema(containerExplanations: false);

        DefinitionProperties(schema, "analysisContainer").ShouldNotContain("summary");
        DefinitionProperties(schema, "analysisContainer").ShouldNotContain("explanation");
        DefinitionProperties(schema, "analysisContainer").ShouldContain("title");

        // The overall summary is the change's, not a container's, and is always asked for.
        Properties(schema).ShouldContain("summary");
        Required(schema).ShouldContain("summary");
    }

    [Fact]
    public void Every_opt_out_together_leaves_the_graph_and_nothing_else()
    {
        var schema = AnalysisResponseSchema.For(new AnalysisRunOptions
        {
            ChangeClusters = false,
            ImplementationGroups = false,
            Risks = false,
            NodeExplanations = false,
            EdgeExplanations = false,
            ContainerExplanations = false,
        });

        Properties(schema).ShouldBe(["summary", "dependencyContainers", "dependencyReadingOrder", "nodes", "edges"], ignoreOrder: true);
        DefinitionProperties(schema, "analysisEdge").ShouldBe(["sourceNodeId", "targetNodeId", "kind"], ignoreOrder: true);

        // At least a third smaller than the full schema — and a reviewer saves that on every turn,
        // twice. What is left is mostly the node and container shapes, which the graph needs.
        (schema.Length * 3).ShouldBeLessThan(Schema().Length * 2);
    }

    [Fact]
    public void The_same_options_are_answered_with_the_same_text()
    {
        // Cached per distinct set of parts, so the per-turn cost of building it is paid once.
        var options = AnalysisRunOptions.Default with { Risks = false, EdgeExplanations = false };

        AnalysisResponseSchema.For(options).ShouldBeSameAs(AnalysisResponseSchema.For(options with { }));
    }

    [Fact]
    public void An_answer_to_a_schema_without_prose_reads_back_as_a_document()
    {
        // System.Text.Json enforces C#'s required keyword, so the prose fields cannot be required on
        // the document record — an answer to a schema that never had them would fail to deserialise.
        const string answer =
            """
            {
              "summary": "Adds a tenant to the cache key.",
              "dependencyContainers": [
                { "id": "cache", "title": "Cache key", "displayOrder": 1, "entryNodeId": "src/Cache.cs", "nodeIds": ["src/Cache.cs"] }
              ],
              "dependencyReadingOrder": ["src/Cache.cs"],
              "nodes": [
                { "id": "src/Cache.cs", "filePath": "src/Cache.cs", "symbol": "", "startLine": 0, "endLine": 0,
                  "title": "Key includes tenant", "importance": 3, "states": ["changed"] }
              ],
              "edges": []
            }
            """;

        var document = JsonSerializer.Deserialize<Analyses.AnalysisResult>(answer, WebJson);

        document.ShouldNotBeNull();
        document.Nodes.ShouldHaveSingleItem().WhatChanged.ShouldBeEmpty();
        document.DependencyContainers.ShouldHaveSingleItem().Explanation.ShouldBeEmpty();
        document.OverallRisks.ShouldBeEmpty();
        document.ImplementationGroups.ShouldBeNull();
    }

    private static string Schema(
        bool changeClusters = true,
        bool implementationGroups = true,
        bool risks = true,
        bool nodeExplanations = true,
        bool edgeExplanations = true,
        bool containerExplanations = true) =>
        AnalysisResponseSchema.For(new AnalysisRunOptions
        {
            ChangeClusters = changeClusters,
            ImplementationGroups = implementationGroups,
            Risks = risks,
            NodeExplanations = nodeExplanations,
            EdgeExplanations = edgeExplanations,
            ContainerExplanations = containerExplanations,
        });

    /// <summary>All sixty-four combinations of the six parts that shape the schema.</summary>
    private static IEnumerable<AnalysisRunOptions> EveryVariant()
    {
        for (var bits = 0; bits < 64; bits++)
        {
            yield return new AnalysisRunOptions
            {
                ChangeClusters = (bits & 1) != 0,
                ImplementationGroups = (bits & 2) != 0,
                Risks = (bits & 4) != 0,
                NodeExplanations = (bits & 8) != 0,
                EdgeExplanations = (bits & 16) != 0,
                ContainerExplanations = (bits & 32) != 0,
            };
        }
    }

    private static void RequiredAreDefined(JsonObject schema)
    {
        var properties = (schema["properties"] as JsonObject ?? []).Select(static entry => entry.Key).ToHashSet();

        foreach (var name in (schema["required"] as JsonArray ?? []).Select(static entry => entry!.GetValue<string>()))
        {
            properties.ShouldContain(name);
        }
    }

    private static string[] StateValues(string schema) =>
        [.. JsonNode.Parse(schema)!["$defs"]!["analysisNode"]!["properties"]!["states"]!["items"]!["enum"]!
            .AsArray()
            .Select(static entry => entry!.GetValue<string>())];

    private static string[] DefinitionProperties(string schema, string definition) =>
        [.. (JsonNode.Parse(schema)!["$defs"]![definition]!["properties"] as JsonObject ?? [])
            .Select(static entry => entry.Key)];

    private static string[] Required(string schema) =>
        [.. (JsonNode.Parse(schema)!.AsObject()["required"] as JsonArray ?? [])
            .Select(static entry => entry!.GetValue<string>())];

    private static string[] Definitions(string schema) =>
        [.. (JsonNode.Parse(schema)!.AsObject()["$defs"] as JsonObject ?? [])
            .Select(static entry => entry.Key)];

    private static string[] Properties(string schema) =>
        [.. (JsonNode.Parse(schema)!.AsObject()["properties"] as JsonObject ?? [])
            .Select(static entry => entry.Key)];
}
