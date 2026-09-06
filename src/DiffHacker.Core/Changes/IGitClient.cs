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

    /// <summary>
    /// Every file git can see: tracked, plus untracked files that <c>.gitignore</c> does not
    /// cover. Sorted, repository-relative, forward slashes.
    /// <para>
    /// Iteration 5 makes this list the toolbox's entire visible universe, which is what keeps
    /// <c>node_modules</c> and build output out of the LLM's context without reimplementing
    /// <c>.gitignore</c> — git's own exclude rules decide, and they decide once.
    /// </para>
    /// </summary>
    /// <exception cref="GitClientException">Git could not be run, or the repository is unreadable.</exception>
    Task<IReadOnlyList<string>> ListFilesAsync(FileListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Regex search across the files <see cref="ListFilesAsync"/> would list.
    /// <para>
    /// Finding no matches is a result, not a failure.
    /// </para>
    /// </summary>
    /// <exception cref="GitClientException">Git could not be run, or the repository is unreadable.</exception>
    Task<GrepResult> GrepAsync(GrepQuery query, CancellationToken cancellationToken);
}

/// <param name="RepositoryPath">Absolute path of the worktree root.</param>
public readonly record struct FileListQuery(string RepositoryPath);

/// <summary>Which regular-expression dialect the caller wrote the pattern in.</summary>
public enum GrepSyntax
{
    /// <summary>Not a regex at all — match the pattern literally.</summary>
    Fixed,

    /// <summary>POSIX extended regular expressions. Always available.</summary>
    Extended,

    /// <summary>
    /// Perl-compatible. What a model writes by habit — <c>\d</c>, <c>\w</c>, lookarounds — but git
    /// is only built with PCRE support sometimes, so a request for it may be answered in
    /// <see cref="Extended"/> instead and said so in <see cref="GrepResult.SyntaxUsed"/>.
    /// </summary>
    Perl,
}

/// <summary>What to search for, and how much of the answer to keep.</summary>
public sealed record GrepQuery
{
    /// <summary>Absolute path of the worktree root.</summary>
    public required string RepositoryPath { get; init; }

    /// <summary>The pattern, in the dialect named by <see cref="Syntax"/>.</summary>
    public required string Pattern { get; init; }

    public GrepSyntax Syntax { get; init; } = GrepSyntax.Extended;

    public bool CaseSensitive { get; init; } = true;

    /// <summary>A git pathspec glob such as <c>src/**/*.ts</c>, or null for the whole repository.</summary>
    public string? PathGlob { get; init; }

    /// <summary>How many matches to skip before keeping any. How pagination is served.</summary>
    public int Skip { get; init; }

    /// <summary>How many matches to keep once <see cref="Skip"/> is satisfied.</summary>
    public int Take { get; init; } = 100;

    /// <summary>
    /// Stop reading after this many matches even though the count would otherwise continue.
    /// <para>
    /// A pattern like <c>.</c> matches every line in the repository, and counting all of them
    /// exactly is worth neither the time nor the memory. Past this the count is reported as a
    /// lower bound (<see cref="GrepResult.CountIsExact"/>).
    /// </para>
    /// </summary>
    public int ScanCeiling { get; init; } = DefaultScanCeiling;

    /// <summary>The default for <see cref="ScanCeiling"/>.</summary>
    public const int DefaultScanCeiling = 100_000;
}

/// <summary>One matching line.</summary>
/// <param name="Path">Repository-relative, forward slashes.</param>
/// <param name="LineNumber">One-based, as git counts.</param>
/// <param name="Line">The matching line, without its terminator.</param>
public readonly record struct GrepMatch(string Path, int LineNumber, string Line);

/// <summary>
/// The window of matches the caller asked for, plus enough context to describe the whole.
/// <para>
/// Matches are counted past the window in the same spirit as the content cap: "40 of 217" is a
/// useful thing to tell a model, "40" on its own invites it to believe it has seen everything.
/// </para>
/// </summary>
public sealed record GrepResult
{
    public required IReadOnlyList<GrepMatch> Matches { get; init; }

    /// <summary>Total matches found, or the scan ceiling when <see cref="CountIsExact"/> is false.</summary>
    public required int TotalMatches { get; init; }

    /// <summary>How many distinct files the counted matches came from.</summary>
    public required int FileCount { get; init; }

    /// <summary>False when the search stopped at <see cref="GrepQuery.ScanCeiling"/>.</summary>
    public required bool CountIsExact { get; init; }

    /// <summary>
    /// The dialect actually used. Differs from the request only when Perl was asked for and this
    /// git was built without PCRE.
    /// </summary>
    public required GrepSyntax SyntaxUsed { get; init; }

    /// <summary>Set when git rejected the pattern itself, so the caller can hand it back verbatim.</summary>
    public string? PatternError { get; init; }
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
