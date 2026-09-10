using System.Text.Json;
using System.Text.Json.Nodes;
using DiffHacker.Contracts;
using DiffHacker.Core.Analyses;

namespace DiffHacker.Core.Tests;

/// <summary>
/// The two shapes a run can ask the model to answer in.
/// <para>
/// The variant matters more than its size suggests. The response schema is over half the fixed
/// preamble of every request, and <c>StructuredOutput</c> sends it twice — as the provider's
/// response format and appended to the system prompt — on each of up to three hundred turns. So
/// "the reviewer does not want the second grouping" has to mean the schema does not mention it.
/// </para>
/// </summary>
public sealed class AnalysisResponseSchemaTests
{
    [Fact]
    public void Asking_for_both_groupings_sends_the_contract_exactly_as_it_is_written()
    {
        // Not a copy, not a rewrite: the schema in /schema is the contract the validator enforces,
        // and the default path must not be able to drift from it.
        AnalysisResponseSchema.For(changeClusters: true)
            .ShouldBe(ContractSchemas.Get(AnalysisPrompt.SchemaKey));
    }

    [Fact]
    public void Dropping_the_second_grouping_removes_exactly_two_properties()
    {
        var full = Properties(AnalysisResponseSchema.For(changeClusters: true));
        var reduced = Properties(AnalysisResponseSchema.For(changeClusters: false));

        full.Except(reduced, StringComparer.Ordinal)
            .ShouldBe(["clusterContainers", "clusterReadingOrder"], ignoreOrder: true);

        reduced.Except(full, StringComparer.Ordinal).ShouldBeEmpty();
    }

    [Fact]
    public void The_removed_properties_are_taken_out_of_required_as_well_as_out_of_properties()
    {
        // A property listed as required but not defined is a schema a provider rejects, and it would
        // fail on the first turn of every opt-out run.
        var root = JsonNode.Parse(AnalysisResponseSchema.For(changeClusters: false))!.AsObject();

        var required = (root["required"] as JsonArray ?? [])
            .Select(static entry => entry!.GetValue<string>())
            .ToArray();

        required.ShouldNotContain("clusterContainers");
        required.ShouldNotContain("clusterReadingOrder");
        required.ShouldContain("dependencyContainers");
        required.ShouldContain("nodes");

        // Every remaining required property is still defined.
        var properties = Properties(AnalysisResponseSchema.For(changeClusters: false));

        foreach (var name in required)
        {
            properties.ShouldContain(name);
        }
    }

    [Fact]
    public void The_container_definition_survives_because_the_first_grouping_still_needs_it()
    {
        var root = JsonNode.Parse(AnalysisResponseSchema.For(changeClusters: false))!.AsObject();

        (root["$defs"] as JsonObject).ShouldNotBeNull()
            .Select(static entry => entry.Key)
            .ShouldContain("analysisContainer");
    }

    [Fact]
    public void The_smaller_schema_is_measurably_smaller()
    {
        // The whole point, stated as a number. It is sent twice per request, so the saving is twice
        // this on every turn.
        AnalysisResponseSchema.For(changeClusters: false).Length
            .ShouldBeLessThan(AnalysisResponseSchema.For(changeClusters: true).Length);
    }

    [Fact]
    public void Both_variants_are_still_valid_JSON()
    {
        foreach (var changeClusters in new[] { true, false })
        {
            Should.NotThrow(() => JsonDocument.Parse(AnalysisResponseSchema.For(changeClusters)));
        }
    }

    private static string[] Properties(string schema) =>
        [.. (JsonNode.Parse(schema)!.AsObject()["properties"] as JsonObject ?? [])
            .Select(static entry => entry.Key)];
}
