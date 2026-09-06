namespace DiffHacker.Core.Changes;

/// <summary>
/// One file in the changeset, described by metadata only.
/// <para>
/// There is no content and no diff on this record, and that is the point: Iteration 3
/// requirement 8 forbids holding a whole changeset in memory, so a 1500-file changeset is 1500
/// of these — a few hundred bytes each — and content is fetched per file through
/// <see cref="IGitClient"/> when something actually needs it.
/// </para>
/// </summary>
public sealed record ChangedFile
{
    /// <summary>Repository-relative path, forward slashes, exactly as git spells it.</summary>
    public required string Path { get; init; }

    /// <summary>
    /// Where a renamed or copied file came from. Null for every other status.
    /// </summary>
    public string? PreviousPath { get; init; }

    public required ChangeStatus Status { get; init; }

    /// <summary>
    /// Lines added, or null when the number is not knowable — a binary file or a submodule
    /// pointer. Null rather than zero: "we did not count" and "we counted nothing" are
    /// different claims, and requirement 4 of the verification list forbids inventing counts.
    /// </summary>
    public int? LinesAdded { get; init; }

    /// <inheritdoc cref="LinesAdded"/>
    public int? LinesRemoved { get; init; }

    /// <summary>
    /// Contiguous changed regions, or null when they could not be attributed. Also null for
    /// binaries and submodules.
    /// </summary>
    public int? HunkCount { get; init; }

    /// <summary>True when git treats the content as binary and will not produce a text diff.</summary>
    public required bool IsBinary { get; init; }

    /// <summary>
    /// True when this entry is a submodule pointer rather than a file. It is one entry, flagged,
    /// so the completeness invariant (§0.2.5) holds without the graph pretending another
    /// repository is a source file.
    /// </summary>
    public bool IsSubmodule { get; init; }

    /// <summary>The submodule commit recorded at <c>HEAD</c>, when this is a submodule.</summary>
    public string? SubmoduleFromCommit { get; init; }

    /// <summary>The submodule commit currently checked out, when this is a submodule.</summary>
    public string? SubmoduleToCommit { get; init; }

    /// <summary>True when either side of the comparison is a symbolic link.</summary>
    public bool IsSymlink { get; init; }

    /// <summary>
    /// True when the file is untracked — present in the working tree, unknown to git, and not
    /// ignored. Always paired with <see cref="ChangeStatus.Added"/>.
    /// </summary>
    public bool IsUntracked { get; init; }

    /// <summary>
    /// True for an untracked directory that is itself a git repository. Git reports it as one
    /// entry rather than listing its files, and there is nothing useful to count inside it.
    /// </summary>
    public bool IsNestedRepository { get; init; }

    /// <summary>
    /// Detected language, or null when the extension is unrecognised. Metadata only: nothing in
    /// this application parses a file according to its language (§0.2.3).
    /// </summary>
    public string? Language { get; init; }

    /// <summary>Which project or module this file belongs to.</summary>
    public required ProjectReference Project { get; init; }
}
