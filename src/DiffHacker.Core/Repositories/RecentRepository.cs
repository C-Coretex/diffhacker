namespace DiffHacker.Core.Repositories;

/// <summary>
/// A repository the user has opened before.
/// <para>
/// The entry outlives the directory: a repository that has been deleted or moved is still
/// listed, marked unavailable, and can be forgotten. Silently dropping it would leave the
/// user wondering where it went.
/// </para>
/// </summary>
public sealed record RecentRepository
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset LastOpenedUtc { get; init; }

    /// <summary>
    /// Whether the path still exists and still looks like a working tree. Evaluated when the
    /// list is read, never persisted.
    /// </summary>
    public bool Available { get; init; }
}
