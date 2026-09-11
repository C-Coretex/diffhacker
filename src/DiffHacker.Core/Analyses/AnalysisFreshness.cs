using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Analyses;

/// <summary>How closely a freshness check could compare files.</summary>
public enum AnalysisFreshnessBasis
{
    /// <summary>Every file present on both sides was compared by content hash.</summary>
    Content,

    /// <summary>
    /// At least one file had no hash on one side and was compared by its line counts instead. An
    /// edit that keeps both counts the same goes unseen, and the screen says the check was weaker.
    /// </summary>
    LineCounts,

    /// <summary>The analysis predates per-file records, so only HEAD could be compared.</summary>
    HeadOnly,
}

/// <summary>
/// Whether the working tree still is the change a stored analysis describes.
/// <para>
/// Every list is sorted and complete. Capping them for the screen is the wire's business; this
/// record is also what the log and the tests read.
/// </para>
/// </summary>
public sealed record AnalysisFreshnessReport
{
    public required string AnalysisId { get; init; }

    public required AnalysisFreshnessBasis Basis { get; init; }

    /// <summary>HEAD is not the commit the analysis ran against.</summary>
    public required bool HeadMoved { get; init; }

    public string? RecordedHeadCommit { get; init; }

    public string? CurrentHeadCommit { get; init; }

    /// <summary>Files in both changesets whose content, status or previous path differs.</summary>
    public IReadOnlyList<string> Modified { get; init; } = [];

    /// <summary>Files changed now that the analysis does not cover.</summary>
    public IReadOnlyList<string> Added { get; init; } = [];

    /// <summary>Files the analysis covers that are no longer changed.</summary>
    public IReadOnlyList<string> Removed { get; init; } = [];

    public required DateTimeOffset CheckedAtUtc { get; init; }

    /// <summary>
    /// Any difference at all. There is no threshold: an analysis is a description of one exact
    /// changeset, and one file edited since is one explanation on screen that may now be wrong.
    /// </summary>
    public bool IsStale => HeadMoved || Modified.Count > 0 || Added.Count > 0 || Removed.Count > 0;
}

/// <summary>
/// The comparison behind <see cref="AnalysisFreshnessReport"/>, pure so it can be tested without a
/// repository.
/// <para>
/// Keyed on path, because a node's identity is (§0.6). A file is modified when its status or
/// previous path changed, or — when both sides carry one — its content hash did; failing a hash,
/// when its added and removed line counts did. HEAD moving is always stale: the committed side of
/// every diff moved with it, whatever the working tree did.
/// </para>
/// </summary>
public static class AnalysisFreshnessCalculator
{
    public static AnalysisFreshnessReport Evaluate(
        Analysis stored,
        Changeset current,
        string? currentHead,
        DateTimeOffset checkedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(current);

        var headMoved = !string.Equals(stored.HeadCommit, currentHead, StringComparison.Ordinal);

        if (stored.ChangedFiles.Count == 0)
        {
            return HeadOnly(stored, currentHead, headMoved, checkedAtUtc);
        }

        var recorded = new Dictionary<string, ChangedFileFacts>(StringComparer.Ordinal);

        foreach (var file in stored.ChangedFiles)
        {
            recorded[file.Path] = file;
        }

        var now = new Dictionary<string, ChangedFile>(StringComparer.Ordinal);

        foreach (var file in current.Files)
        {
            now[file.Path] = file;
        }

        var modified = new List<string>();
        var added = new List<string>();
        var basis = AnalysisFreshnessBasis.Content;

        foreach (var (path, file) in now)
        {
            if (!recorded.TryGetValue(path, out var then))
            {
                added.Add(path);
                continue;
            }

            var (differs, byContent) = Compare(then, file);

            if (!byContent)
            {
                basis = AnalysisFreshnessBasis.LineCounts;
            }

            if (differs)
            {
                modified.Add(path);
            }
        }

        var removed = recorded.Keys.Where(path => !now.ContainsKey(path)).ToList();

        modified.Sort(StringComparer.Ordinal);
        added.Sort(StringComparer.Ordinal);
        removed.Sort(StringComparer.Ordinal);

        return new AnalysisFreshnessReport
        {
            AnalysisId = stored.Id,
            Basis = basis,
            HeadMoved = headMoved,
            RecordedHeadCommit = stored.HeadCommit,
            CurrentHeadCommit = currentHead,
            Modified = modified,
            Added = added,
            Removed = removed,
            CheckedAtUtc = checkedAtUtc,
        };
    }

    /// <summary>For an analysis with no per-file records, where the changeset is not worth reading.</summary>
    public static AnalysisFreshnessReport HeadOnly(
        Analysis stored,
        string? currentHead,
        DateTimeOffset checkedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return HeadOnly(
            stored,
            currentHead,
            !string.Equals(stored.HeadCommit, currentHead, StringComparison.Ordinal),
            checkedAtUtc);
    }

    private static AnalysisFreshnessReport HeadOnly(
        Analysis stored,
        string? currentHead,
        bool headMoved,
        DateTimeOffset checkedAtUtc) => new()
    {
        AnalysisId = stored.Id,
        Basis = AnalysisFreshnessBasis.HeadOnly,
        HeadMoved = headMoved,
        RecordedHeadCommit = stored.HeadCommit,
        CurrentHeadCommit = currentHead,
        CheckedAtUtc = checkedAtUtc,
    };

    /// <summary>
    /// Whether one file differs, and whether that was decided by content.
    /// <para>
    /// A deleted file has no working-tree side on either day, so its missing hash is not a weaker
    /// comparison — there is nothing to hash, and its status and line counts say everything.
    /// </para>
    /// </summary>
    private static (bool Differs, bool ByContent) Compare(ChangedFileFacts then, ChangedFile now)
    {
        if (then.Status != now.Status
            || !string.Equals(then.PreviousPath, now.PreviousPath, StringComparison.Ordinal))
        {
            return (true, true);
        }

        if (then.ContentSha256 is { } before && now.ContentSha256 is { } after)
        {
            return (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase), true);
        }

        var differs = then.LinesAdded != now.LinesAdded
            || then.LinesRemoved != now.LinesRemoved
            || then.IsBinary != now.IsBinary;

        return (differs, now.Status is ChangeStatus.Deleted);
    }
}

/// <summary>
/// Reads the working tree and asks <see cref="AnalysisFreshnessCalculator"/> whether a stored
/// analysis still describes it. Reads, never runs: nothing here reaches a provider.
/// </summary>
public sealed class AnalysisFreshnessChecker(IGitClient git, TimeProvider clock)
{
    /// <exception cref="GitClientException">Git could not be run, or the repository is unreadable.</exception>
    public async Task<AnalysisFreshnessReport> CheckAsync(Analysis analysis, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var head = await git.GetHeadCommitAsync(analysis.RepositoryPath, cancellationToken).ConfigureAwait(false);

        // With nothing per file to compare against, reading and hashing the changeset would be work
        // whose answer is thrown away.
        if (analysis.ChangedFiles.Count == 0)
        {
            return AnalysisFreshnessCalculator.HeadOnly(analysis, head, clock.GetUtcNow());
        }

        // The same query the run made — untracked included, content hashed — or the two sides of the
        // comparison would disagree about what the changeset is.
        var changeset = await git
            .GetChangesetAsync(new ChangesetQuery(analysis.RepositoryPath, HashContent: true), cancellationToken)
            .ConfigureAwait(false);

        return AnalysisFreshnessCalculator.Evaluate(analysis, changeset, head, clock.GetUtcNow());
    }
}
