using DiffHacker.Core.Analyses;

namespace DiffHacker.Core.Settings;

/// <summary>
/// Persistence for completed analyses.
/// <para>
/// There is no method here for saving a partial one, and that is the design rather than an
/// omission: a run that was cancelled or that failed validation has nothing to store, and an
/// interface that could not express it cannot accidentally be made to. Reopening an analysis reads
/// from here and never starts a conversation — the money was spent once.
/// </para>
/// <para>
/// <see cref="SetNodesReviewedAsync"/> and <see cref="SetGroupingAsync"/> are the two methods here
/// that change a stored analysis, and neither is an exception to the paragraph above: both write the
/// reviewer's own state — which nodes they have read, and which grouping they are reading it in —
/// and both live beside the model's answer rather than inside it. <b>Nothing here can edit the
/// document</b>, and that is the invariant to keep if a third one is ever added.
/// </para>
/// </summary>
public interface IAnalysisStore
{
    /// <summary>Stores a completed analysis, pruning the oldest so the history stays bounded.</summary>
    ValueTask SaveAsync(Analysis analysis, CancellationToken cancellationToken);

    /// <summary>The most recent analysis of a repository, or null when there is none.</summary>
    ValueTask<Analysis?> GetLatestAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>One analysis by id, or null. Ids are unique across repositories.</summary>
    ValueTask<Analysis?> FindAsync(string analysisId, CancellationToken cancellationToken);

    /// <summary>Analyses of one repository, most recent first.</summary>
    ValueTask<IReadOnlyList<Analysis>> ListAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>Forgets every analysis of a repository.</summary>
    ValueTask DeleteAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>
    /// Marks or unmarks nodes of one analysis as reviewed, and returns every id now marked — not
    /// only the ones this call touched — so the caller never has to read back what it just wrote.
    /// <para>
    /// Ids are taken on trust here. Whether a node belongs to the analysis is a question about the
    /// document, and the caller holding the document is the one that can answer it.
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyList<string>> SetNodesReviewedAsync(
        string analysisId,
        IReadOnlyList<string> nodeIds,
        bool reviewed,
        CancellationToken cancellationToken);

    /// <summary>
    /// Remembers which of its groupings one analysis is being read in, so reopening it later shows
    /// the picture the reviewer left rather than the default.
    /// <para>
    /// Whether the analysis actually holds that grouping is not asked here, on the same grounds node
    /// ids are taken on trust above: it is a question about the model's answer, and the caller
    /// holding the document is the one that can answer it.
    /// </para>
    /// </summary>
    ValueTask SetGroupingAsync(
        string analysisId,
        AnalysisGrouping grouping,
        CancellationToken cancellationToken);
}
