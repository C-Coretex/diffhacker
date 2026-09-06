namespace DiffHacker.Core.Changes;

/// <summary>
/// What happened to one file between <c>HEAD</c> and the working tree.
/// <para>
/// Deliberately five values, matching Iteration 3 requirement 3. Git's raw output also carries
/// <c>T</c> (the type changed — a file became a symlink, say) and <c>U</c> (unmerged, mid
/// conflict). Both are reported as <see cref="Modified"/> and logged: they are modifications
/// from a reviewer's point of view, and inventing statuses for them would put vocabulary in the
/// graph that nothing downstream knows how to render.
/// </para>
/// </summary>
public enum ChangeStatus
{
    /// <summary>The file does not exist at <c>HEAD</c> and does in the working tree.</summary>
    Added,

    /// <summary>The file exists on both sides with different content, mode or submodule commit.</summary>
    Modified,

    /// <summary>The file exists at <c>HEAD</c> and not in the working tree.</summary>
    Deleted,

    /// <summary>
    /// The file moved. <see cref="ChangedFile.PreviousPath"/> carries where it came from, so a
    /// rename is never shown as an unrelated delete plus add.
    /// </summary>
    Renamed,

    /// <summary>The file was copied from another file that <see cref="ChangedFile.PreviousPath"/> names.</summary>
    Copied,
}
