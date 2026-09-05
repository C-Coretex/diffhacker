namespace DiffHacker.Core.Repositories;

/// <summary>
/// A git working tree the application has accepted. <see cref="Path"/> is always the worktree
/// root, never a subdirectory the user happened to pick.
/// </summary>
public sealed record RepositoryDescriptor
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// False for a repository with no commits, where there is no <c>HEAD</c> to compare the
    /// working tree against. Accepted here; Iteration 3 decides what a diff means for it.
    /// </summary>
    public required bool HasCommits { get; init; }

    public required bool IsLinkedWorktree { get; init; }
}

/// <summary>
/// Why a chosen path was not accepted. Each value maps to a stable RPC error code, so the
/// renderer can say something specific rather than "that did not work".
/// </summary>
public enum RepositoryRejection
{
    /// <summary>The path was accepted.</summary>
    None,

    /// <summary>Nothing exists at that path, or it is a file rather than a directory.</summary>
    PathNotFound,

    /// <summary>The path exists but is not inside a git working tree.</summary>
    NotARepository,

    /// <summary>
    /// A bare repository. It has no working tree, so working-tree-vs-HEAD — the only thing
    /// this application reviews (§0.2.11) — is meaningless for it.
    /// </summary>
    BareRepository,

    /// <summary>The path could not be read.</summary>
    AccessDenied,

    /// <summary>No usable git executable was found, so nothing could be validated.</summary>
    GitUnavailable,
}

/// <summary>Result of resolving a user-chosen path to a working tree.</summary>
public sealed record RepositoryResolution
{
    public RepositoryDescriptor? Repository { get; init; }

    public required RepositoryRejection Rejection { get; init; }

    /// <summary>
    /// True when the user picked a directory inside the repository and this resolved upwards
    /// to the worktree root. Surfaced rather than applied silently.
    /// </summary>
    public bool NormalizedFromSubdirectory { get; init; }

    public static RepositoryResolution Accepted(RepositoryDescriptor repository, bool normalized) =>
        new()
        {
            Repository = repository,
            Rejection = RepositoryRejection.None,
            NormalizedFromSubdirectory = normalized,
        };

    public static RepositoryResolution Rejected(RepositoryRejection rejection) =>
        new() { Rejection = rejection };
}
