namespace DiffHacker.Core.Changes;

/// <summary>
/// Read-only access to a repository's current change.
/// <para>
/// Three operations, and the split between them is the whole memory strategy:
/// <see cref="GetChangesetAsync"/> returns metadata for every changed file and no content at
/// all, and the other two fetch one file's content or diff when something needs it. That is how
/// requirement 2 ("untracked files carry their full content as the added side") and
/// requirement 8 ("do not load an entire large changeset into memory") both hold.
/// </para>
/// <para>
/// Nothing behind this interface may modify a repository (§0.2.12). The git subcommand allowlist
/// enforces it rather than trusting call sites to be careful.
/// </para>
/// </summary>
public interface IGitClient
{
    /// <summary>
    /// Produces the working tree compared against <c>HEAD</c>.
    /// </summary>
    /// <exception cref="GitClientException">Git could not be run, or the repository is unreadable.</exception>
    Task<Changeset> GetChangesetAsync(ChangesetQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// The unified diff for one file. Capped at <see cref="ContentLimits.MaxBytes"/>.
    /// </summary>
    /// <exception cref="GitClientException">Git could not be run, or the repository is unreadable.</exception>
    Task<FileDiffResult> GetFileDiffAsync(FileDiffQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// One file's content on one side of the comparison. Capped at
    /// <see cref="ContentLimits.MaxBytes"/>; an absent side is an ordinary result, not an error.
    /// </summary>
    /// <exception cref="GitClientException">Git could not be run, or the repository is unreadable.</exception>
    Task<FileContentResult> GetFileContentAsync(FileContentQuery query, CancellationToken cancellationToken);
}

/// <param name="RepositoryPath">Absolute path of the worktree root.</param>
/// <param name="IncludeUntracked">
/// Requirement 2's toggle. Defaults to included, because an AI-generated change routinely adds
/// brand-new files and dropping them would silently violate §0.2.5. Gitignored files are never
/// included either way.
/// </param>
public readonly record struct ChangesetQuery(string RepositoryPath, bool IncludeUntracked = true);

/// <param name="RepositoryPath">Absolute path of the worktree root.</param>
/// <param name="Path">Repository-relative path of the file to diff.</param>
/// <param name="PreviousPath">
/// The file's path at <c>HEAD</c> when it was renamed or copied. Passed to git so the diff shows
/// the move rather than an unrelated add.
/// </param>
/// <param name="Untracked">
/// True when the file is untracked. Git has nothing to diff against for those, so the added side
/// is produced from the file itself.
/// </param>
public readonly record struct FileDiffQuery(
    string RepositoryPath,
    string Path,
    string? PreviousPath = null,
    bool Untracked = false);

/// <param name="RepositoryPath">Absolute path of the worktree root.</param>
/// <param name="Path">Repository-relative path of the file to read.</param>
/// <param name="Side">Which side of the comparison to read.</param>
public readonly record struct FileContentQuery(string RepositoryPath, string Path, FileSide Side);

/// <summary>
/// The git command line could not produce an answer.
/// <para>
/// Reserved for genuine failure — git missing, git hung, the repository unreadable. Expected
/// outcomes are results: a clean tree is <see cref="Changeset.IsClean"/>, a missing side is
/// <see cref="FileContentKind.Absent"/>.
/// </para>
/// </summary>
public sealed class GitClientException : Exception
{
    public GitClientException()
    {
    }

    public GitClientException(string message)
        : base(message)
    {
    }

    public GitClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public GitClientException(string message, GitClientFailure failure)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>Which failure this was, so the host can pick a stable RPC error code.</summary>
    public GitClientFailure Failure { get; } = GitClientFailure.Unknown;
}

/// <summary>Why <see cref="IGitClient"/> could not answer. Each value maps to one RPC error code.</summary>
public enum GitClientFailure
{
    Unknown,

    /// <summary>No usable git executable, or git hung and was killed.</summary>
    GitUnavailable,

    /// <summary>The path is not a readable git working tree.</summary>
    RepositoryUnreadable,

    /// <summary>Git ran and failed. Its stderr is on the exception message, for the log only.</summary>
    GitFailed,
}
