using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace DiffHacker.Contracts.Tests;

/// <summary>
/// One container shape twice, one node shape twice, one edge shape twice, and the two enums that
/// travel with them.
/// <para>
/// A schema cannot reference a definition in another file, so the document the model answers with
/// and the view the renderer reads each carry their own copy of the graph. That is a survivable
/// amount of duplication only while something checks the copies cannot drift — which is this.
/// </para>
/// <para>
/// The view's copies carry a few fields the model was never asked for: which container a node is
/// in, and whether an edge crosses one. Those are resolved by the host from what the model did say,
/// so they are named as exceptions here rather than being allowed to hide a real divergence.
/// </para>
/// </summary>
public sealed class AnalysisAgreementTests
{
    [Fact]
    public void The_two_container_shapes_agree()
    {
        Shape<AnalysisContainerInfo>().ShouldBe(Shape<AnalysisContainer>());
    }

    [Fact]
    public void The_two_node_shapes_agree_apart_from_what_the_host_resolves_per_grouping()
    {
        // containerId and rank are both properties of the *active grouping* rather than of the node:
        // the document holds two groupings, so a node sits in one container per grouping and at one
        // position per grouping, and neither could be stated once on the node. The host resolves
        // both from the grouping it is projecting.
        Shape<AnalysisNodeInfo>()
            .Where(static entry => entry.Name is not ("containerId" or "rank" or "states"))
            .ShouldBe(
                Shape<AnalysisNode>().Where(static entry => entry.Name != "states"),
                ignoreOrder: true);

        Names<AnalysisNode>().ShouldNotContain("rank");
        Names<AnalysisNodeInfo>().ShouldContain("rank");

        // The states arrays hold differently-named copies of nearly the same enum, so they are
        // compared by their values instead, just below.
        Names<AnalysisNodeInfo>().ShouldContain("states");
        Names<AnalysisNode>().ShouldContain("states");
    }

    [Fact]
    public void The_result_carries_both_groupings_and_the_view_carries_one_of_them()
    {
        // Two sibling pairs at the root rather than a list of grouping objects, because a definition
        // may not reference another one and a grouping needs the container definition.
        foreach (var field in new[]
        {
            "dependencyContainers", "dependencyReadingOrder", "clusterContainers", "clusterReadingOrder",
        })
        {
            Names<AnalysisResult>().ShouldContain(field);
        }

        // The view is a projection of one of them, so it has one container list and one reading
        // order, and says which grouping they belong to.
        Names<AnalysisView>().ShouldContain("containers");
        Names<AnalysisView>().ShouldContain("readingOrder");
        Names<AnalysisView>().ShouldContain("grouping");
        Names<AnalysisView>().ShouldContain("availableGroupings");
        Names<AnalysisView>().ShouldNotContain("clusterContainers");
    }

    [Fact]
    public void The_two_grouping_enums_agree()
    {
        string[] expected = ["dependency_flow", "change_clusters"];

        WireValues<AnalysisGroupingMode>().ShouldBe(expected, ignoreOrder: true);
        WireValues<SetGroupingMode>().ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void The_two_edge_shapes_agree_apart_from_the_crossing_the_host_computes()
    {
        Shape<AnalysisEdgeInfo>()
            .Where(static entry => entry.Name != "crossesContainers" && entry.Name != "kind")
            .ShouldBe(Shape<AnalysisEdge>().Where(static entry => entry.Name != "kind"), ignoreOrder: true);
    }

    [Fact]
    public void The_two_node_state_enums_agree_apart_from_the_entry_point_the_host_derives()
    {
        // The one deliberate divergence in the pair, and the reason for it: being a starting point is
        // a fact about a container of one grouping, not about the node, so the model no longer says
        // it and could not — a node may start a cluster in one grouping and sit in the middle of
        // another. The host adds it for whichever grouping it is projecting, which is why the wire
        // still carries it and the renderer's badge never changed.
        string[] shared = ["changed", "added", "deleted", "unchanged_relevant", "risky"];

        WireValues<AnalysisNodeState>().ShouldBe(shared, ignoreOrder: true);
        WireValues<AnalysisNodeInfoState>().ShouldBe([.. shared, "entry_point"], ignoreOrder: true);
    }

    [Fact]
    public void The_two_edge_kind_enums_agree()
    {
        string[] expected = ["direct", "conceptual"];

        WireValues<AnalysisEdgeKind>().ShouldBe(expected, ignoreOrder: true);
        WireValues<AnalysisEdgeInfoKind>().ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void The_node_shape_is_the_one_the_renderer_was_written_against()
    {
        // Pinned so that dropping a field from both copies at once is still a failure. Every one
        // of these is either shown to the reviewer or used to lay the diagram out.
        Names<AnalysisNodeInfo>().ShouldBe(
            [
                "id", "containerId", "filePath", "symbol", "startLine", "endLine", "title",
                "whatChanged", "whyItChanged", "howItAffectsOthers", "implementationNotes", "risks",
                "importance", "rank", "states",
            ],
            ignoreOrder: true);

        // And what the model is asked for is that list minus the two the host resolves per grouping.
        Names<AnalysisNode>().ShouldBe(
            [
                "id", "filePath", "symbol", "startLine", "endLine", "title", "whatChanged",
                "whyItChanged", "howItAffectsOthers", "implementationNotes", "risks", "importance",
                "states",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void Risks_are_their_own_field_on_every_shape_that_can_carry_one()
    {
        // §0.2 keeps risks out of the explanations so a reviewer can read them as a column. A
        // schema that merged them would be the quiet way to lose that, so the separation is
        // asserted rather than assumed.
        foreach (var shape in new[] { Names<AnalysisNode>(), Names<AnalysisContainer>(), Names<AnalysisEdge>() })
        {
            shape.ShouldContain("risks");
        }

        Names<AnalysisResult>().ShouldContain("overallRisks");
        Names<AnalysisNodeInfo>().ShouldContain("risks");
    }

    [Fact]
    public void The_per_file_facts_agree_with_the_changeset_they_were_taken_from()
    {
        // A third duplicated shape, and the same reasoning: ChangedFileFactsInfo is the subset of
        // ChangedFileInfo that a node box and the project legend draw, recorded with the analysis
        // rather than re-read. A subset rather than a copy, so this checks that every field it does
        // carry means the same thing on both sides.
        var facts = Shape<ChangedFileFactsInfo>().Where(static entry => entry.Name != "status");
        var source = Shape<ChangedFileInfo>().ToDictionary(entry => entry.Name, entry => entry.Type);

        foreach (var (name, type) in facts)
        {
            source.ShouldContainKey(
                name,
                $"'{name}' has no counterpart on ChangedFileInfo, so the facts stored with an "
                + "analysis would mean something the changeset never said.");

            source[name].ShouldBe(type, $"'{name}' has a different type on each side.");
        }

        // Status is compared by value rather than by type: the two enums are generated with
        // different names for the same wire strings, exactly as the node-state pair is.
        WireValues<ChangedFileFactsInfoStatus>().ShouldBe(
            WireValues<ChangedFileInfoStatus>(),
            ignoreOrder: true);
    }

    [Fact]
    public void The_per_file_facts_are_the_ones_the_node_boxes_were_written_against()
    {
        // Pinned, so dropping one is a decision rather than an accident. Every one of these is on a
        // box: the path it is matched by, the status word, the two line counts, the binary flag
        // that explains their absence, the language, and the project that picks the fill colour.
        //
        // previousPath joined them in Iteration 10, for the diff viewer rather than the box: the
        // committed side of a renamed file lives at the old path, so reading it from the new one
        // would show every rename as an addition.
        Names<ChangedFileFactsInfo>().ShouldBe(
            ["path", "previousPath", "status", "linesAdded", "linesRemoved", "isBinary", "language", "project"],
            ignoreOrder: true);
    }

    [Fact]
    public void Every_diagnostic_severity_the_host_can_report_has_a_wire_value()
    {
        WireValues<AnalysisDiagnosticInfoSeverity>().ShouldBe(["error", "warning"], ignoreOrder: true);
    }

    private static string[] Names<T>() =>
        [.. Shape<T>().Select(static entry => entry.Name)];

    /// <summary>
    /// A record's wire shape: its JSON property names paired with the simple name of each type, so
    /// that two generated copies can be compared without their element types — which differ by
    /// namespace-local name — getting in the way.
    /// </summary>
    private static (string Name, string Type)[] Shape<T>() =>
        [.. typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (
                Name: property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name,
                Type: Describe(property.PropertyType)))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)];

    private static string Describe(Type type) =>
        type.IsGenericType
            ? $"{type.Name}<{string.Join(',', type.GetGenericArguments().Select(argument => argument.Name))}>"
            : type.Name;

    private static string[] WireValues<TEnum>()
        where TEnum : struct, Enum =>
        [.. typeof(TEnum)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetCustomAttributes(typeof(EnumMemberAttribute), false)
                .Cast<EnumMemberAttribute>()
                .FirstOrDefault()?.Value ?? field.Name)];
}
