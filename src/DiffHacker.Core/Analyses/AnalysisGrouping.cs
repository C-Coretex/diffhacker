using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using DiffHacker.Contracts;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// One of the two ways the model groups the same nodes.
/// <para>
/// Both are produced by one run and stored in one document, so switching between them reads the
/// answer differently rather than asking for it again. Which one the reviewer is looking at is the
/// only thing that changes: the nodes, their explanations and the edges between them are shared, and
/// only the clusters, the entry points and the reading order differ.
/// </para>
/// </summary>
[JsonConverter(typeof(SchemaEnumConverter<AnalysisGrouping>))]
public enum AnalysisGrouping
{
    /// <summary>
    /// Clusters that keep a complete change path intact, even where the path crosses concerns. The
    /// more useful of the two and the default.
    /// </summary>
    [EnumMember(Value = "dependency_flow")]
    DependencyFlow,

    /// <summary>
    /// Clusters grouped by theme or concern, which splits a path spanning several of them. A
    /// genuinely complementary view, not a lesser one.
    /// </summary>
    [EnumMember(Value = "change_clusters")]
    ChangeClusters,
}

/// <summary>
/// One grouping of one result: its clusters and its reading order, named.
/// <para>
/// A lens rather than stored state. <see cref="AnalysisResult"/> holds the two groupings as four
/// flat properties because a schema definition may not reference another one, and everything that
/// walks a grouping — the validator, the wire projection, the statistics — asks for one of these
/// instead of choosing between those four properties itself.
/// </para>
/// </summary>
public sealed record AnalysisGroupingView
{
    public required AnalysisGrouping Grouping { get; init; }

    public required IReadOnlyList<AnalysisContainer> Containers { get; init; }

    /// <summary>The order the model stated for this grouping, unresolved. See <see cref="AnalysisReadingOrder"/>.</summary>
    public required IReadOnlyList<string> ReadingOrder { get; init; }
}

/// <summary>The wire spellings, in one place, for the storage column and the JSON-RPC surface.</summary>
public static class AnalysisGroupingNames
{
    public const string DependencyFlow = "dependency_flow";

    public const string ChangeClusters = "change_clusters";

    public static string Of(AnalysisGrouping grouping) => grouping switch
    {
        AnalysisGrouping.DependencyFlow => DependencyFlow,
        AnalysisGrouping.ChangeClusters => ChangeClusters,
        _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "Unnamed grouping."),
    };

    /// <summary>
    /// Reads a stored spelling back, or null when it is absent or unrecognisable. Null rather than a
    /// throw because the value comes from a database column an older or newer build wrote, and a
    /// grouping nobody can name should fall back to the default rather than stop the analysis
    /// opening.
    /// </summary>
    public static AnalysisGrouping? Parse(string? value) => value switch
    {
        DependencyFlow => AnalysisGrouping.DependencyFlow,
        ChangeClusters => AnalysisGrouping.ChangeClusters,
        _ => null,
    };
}
