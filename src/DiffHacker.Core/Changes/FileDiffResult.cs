namespace DiffHacker.Core.Changes;

/// <summary>The unified diff for a single file, or an explanation of why there is not one.</summary>
public sealed record FileDiffResult
{
    /// <summary>
    /// Reuses <see cref="FileContentKind"/> deliberately: a diff request has exactly the same
    /// four outcomes as a content request, and giving the renderer one enum to switch on beats
    /// two that mean the same thing.
    /// </summary>
    public required FileContentKind Kind { get; init; }

    /// <summary>Repository-relative path the diff is for.</summary>
    public required string Path { get; init; }

    /// <summary>Where the file came from, for a rename or a copy.</summary>
    public string? PreviousPath { get; init; }

    /// <summary>
    /// Unified diff text, present only when <see cref="Kind"/> is
    /// <see cref="FileContentKind.Text"/>. Includes the <c>diff --git</c> header, so it can be
    /// fed to anything that reads patches.
    /// </summary>
    public string? UnifiedDiff { get; init; }

    /// <summary>Size of the diff in bytes, or of the file when there is no diff to show.</summary>
    public required long SizeBytes { get; init; }
}
