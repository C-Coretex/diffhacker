using DiffHacker.Core.Llm;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// One complete run: the model's graph, what it cost to produce, and what the application worked
/// out about it.
/// <para>
/// This is what gets stored, and storing it is what makes §0.2.8 affordable — the whole result
/// exists before anything is shown, and reopening it later never starts another conversation.
/// Only a completed, validated run becomes one of these; a cancelled or failed run reports what it
/// spent and stores nothing, so nothing partial can ever be mistaken for a finished analysis.
/// </para>
/// </summary>
public sealed record Analysis
{
    public required string Id { get; init; }

    public required string RepositoryPath { get; init; }

    /// <summary>The contract version the document was written against.</summary>
    public required string SchemaVersion { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>HEAD when the run happened, or null in a repository with no commits.</summary>
    public string? HeadCommit { get; init; }

    public required string ProviderDisplayName { get; init; }

    public required string Model { get; init; }

    public LlmUsage Usage { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>How many times the result was handed back to be repaired. Zero on a clean run.</summary>
    public int RepairRounds { get; init; }

    /// <summary>The model's answer, exactly as it produced it.</summary>
    public required AnalysisResult Document { get; init; }

    /// <summary>
    /// The numbers, computed for the dependency-flow grouping. Use <see cref="StatisticsFor"/> when
    /// showing another one.
    /// </summary>
    public required AnalysisStatistics Statistics { get; init; }

    /// <summary>
    /// The grouping the reviewer last chose for this analysis, or null when they never chose one and
    /// the application-wide default applies.
    /// <para>
    /// The second thing in a stored analysis the user writes rather than the model, and it sits here
    /// for the same reason <see cref="ReviewedNodeIds"/> does: the document is the model's answer
    /// unedited, and which of its two groupings someone is looking at is not part of it. Null on an
    /// analysis written before database schema 7.
    /// </para>
    /// </summary>
    public AnalysisGrouping? Grouping { get; init; }

    /// <summary>
    /// The changeset as it stood when the run happened, one entry per file. Stored with the result
    /// rather than re-read, so a diagram opened next week still describes the change it was made
    /// from. Empty on an analysis written by a build older than schema 1.8, in which case the node
    /// boxes simply show no line counts — an old analysis opens rather than fails.
    /// </summary>
    public IReadOnlyList<ChangedFileFacts> ChangedFiles { get; init; } = [];

    /// <summary>
    /// What validation observed. Only ever warnings: an error would have failed the run.
    /// </summary>
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>
    /// Ids of the nodes the reviewer has marked reviewed. The one part of a stored analysis the user
    /// writes rather than the model, and the reason it sits here rather than inside
    /// <see cref="Document"/>: the document is the model's answer unedited, and this is not part of
    /// it. Keyed by node id — which <see cref="AnalysisNodeId"/> derives from the file path — so a
    /// mark means the same thing after a re-run and after a change of grouping mode. Empty on an
    /// analysis written before schema 6.
    /// </summary>
    public IReadOnlyList<string> ReviewedNodeIds { get; init; } = [];

    /// <summary>
    /// Every tool call in the order the model made them — requirement 7. Kept whole rather than
    /// summarised, because the question it answers later is "what did it actually look at", and a
    /// count cannot answer that.
    /// </summary>
    public IReadOnlyList<LlmToolCallRecord> ToolCalls { get; init; } = [];

    /// <summary>Everything the model said through <c>report_progress</c>, in order.</summary>
    public IReadOnlyList<string> ProgressMessages { get; init; } = [];

    /// <summary>
    /// The traversal to offer the reviewer in one grouping: the model's own reading order for it when
    /// that covered every node, and otherwise the one derived from container order and membership
    /// order. Computed on read rather than stored, so the stored document stays exactly what the
    /// model wrote.
    /// </summary>
    public IReadOnlyList<string> ReadingOrderFor(AnalysisGrouping grouping) =>
        AnalysisReadingOrder.Resolve(Document, grouping);

    /// <summary>
    /// The numbers for one grouping. Recomputed rather than stored twice, for the same reason
    /// <see cref="ReadingOrderFor"/> is: everything it needs is in the document, and a second copy
    /// of twenty numbers is a second thing that can be stale.
    /// </summary>
    public AnalysisStatistics StatisticsFor(AnalysisGrouping grouping)
    {
        if (grouping is AnalysisGrouping.DependencyFlow)
        {
            return Statistics;
        }

        var containers = Document.For(grouping).Containers;
        var sizes = containers.Select(static container => container.NodeIds.Count).ToArray();

        return Statistics with
        {
            ContainerCount = containers.Count,
            LargestContainerSize = sizes.Length == 0 ? 0 : sizes.Max(),
            SmallestContainerSize = sizes.Length == 0 ? 0 : sizes.Min(),
            RiskCount = Statistics.RiskCount
                - Document.DependencyContainers.Sum(static container => container.Risks.Count)
                + containers.Sum(static container => container.Risks.Count),
        };
    }

    /// <summary>Which groupings this analysis can be shown in. See <see cref="AnalysisResult.Groupings"/>.</summary>
    public IReadOnlyList<AnalysisGrouping> AvailableGroupings => Document.Groupings;
}

/// <summary>Why an analysis run stopped, beyond the provider-level <see cref="LlmFailures"/>.</summary>
public static class AnalysisFailures
{
    /// <summary>No LLM provider is configured, so there is nothing to run against.</summary>
    public const string NoProvider = "analysis_no_provider";

    /// <summary>There is nothing to analyse: the working tree matches HEAD.</summary>
    public const string CleanChangeset = "analysis_clean_changeset";

    /// <summary>The answer validated against the schema but could not be read back.</summary>
    public const string UnreadableAnswer = "analysis_unreadable_answer";

    /// <summary>
    /// The result kept failing validation and the repair rounds ran out. The diagnostics say what
    /// was wrong, and nothing was stored.
    /// </summary>
    public const string ValidationFailed = "analysis_validation_failed";

    /// <summary>Every code above, for tests that assert the taxonomy and the catalogue agree.</summary>
    public static IReadOnlyList<string> All { get; } =
        [NoProvider, CleanChangeset, UnreadableAnswer, ValidationFailed];
}
