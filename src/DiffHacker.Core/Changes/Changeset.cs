namespace DiffHacker.Core.Changes;

/// <summary>
/// The working tree compared against <c>HEAD</c>: the one artifact every later iteration
/// consumes.
/// <para>
/// Covers staged and unstaged modifications together, plus untracked files that are not
/// gitignored. There is no branch, no commit range and no merge base anywhere in this type —
/// §0.2.11 fixes the scope at local uncommitted change.
/// </para>
/// </summary>
public sealed record Changeset
{
    /// <summary>Absolute path of the worktree root the changeset was taken from.</summary>
    public required string RepositoryPath { get; init; }

    /// <summary>
    /// True when the working tree matches <c>HEAD</c> exactly. Reported so the interface can say
    /// "nothing to review" rather than showing an empty list that looks like a failure
    /// (requirement 9).
    /// </summary>
    public required bool IsClean { get; init; }

    /// <summary>
    /// False for a repository with no commits. Everything is then compared against the empty
    /// tree, so every file reads as added.
    /// </summary>
    public required bool HasCommits { get; init; }

    /// <summary>Whether untracked files were included in this run (requirement 2's toggle).</summary>
    public required bool UntrackedIncluded { get; init; }

    /// <summary>
    /// Every changed file, ordered as git reported it, with untracked files appended. Metadata
    /// only — see <see cref="ChangedFile"/>.
    /// </summary>
    public required IReadOnlyList<ChangedFile> Files { get; init; }

    public required ChangesetStatistics Statistics { get; init; }

    /// <summary>
    /// False when hunk counts could not be attributed to files with confidence, in which case
    /// every <see cref="ChangedFile.HunkCount"/> is null. Saying so beats reporting numbers
    /// against the wrong files.
    /// </summary>
    public required bool HunkCountsAvailable { get; init; }
}
