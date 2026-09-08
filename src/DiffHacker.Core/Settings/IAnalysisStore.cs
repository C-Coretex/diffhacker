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
}
