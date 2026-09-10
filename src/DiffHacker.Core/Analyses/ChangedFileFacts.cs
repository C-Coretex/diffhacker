using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// What git said about one changed file at the moment the run happened.
/// <para>
/// The model's answer says what a change <i>means</i>; it never says how many lines moved or which
/// project the file sits in, and it should not — those are facts the application already has. But
/// the node boxes need them, so they have to survive the run.
/// </para>
/// <para>
/// <b>Recorded, not re-read.</b> The obvious alternative is to take today's <see cref="Changeset"/>
/// and join it against a stored analysis by path. That prints today's line counts on yesterday's
/// analysis, and after one more edit the diagram quietly describes a change that no longer exists.
/// An analysis is a photograph of one changeset; these are part of the photograph.
/// </para>
/// <para>
/// A subset of <see cref="ChangedFile"/> — the fields a node box and the project legend draw —
/// rather than the whole record, because everything else on it (submodule commits, symlink and
/// nested-repository flags, hunk counts) is either already visible in the graph or of no use once
/// the run is over, and this list is stored per analysis at one entry per changed file.
/// </para>
/// </summary>
public sealed record ChangedFileFacts
{
    /// <summary>
    /// Repository-relative path, forward slashes, exactly as git spells it — and exactly as
    /// <see cref="AnalysisNode.FilePath"/> spells it, which is what lets the renderer index by it.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Where the file was before it moved, or null when it did not. Kept because the committed side
    /// of a renamed file lives at the old path — reading it from the new one finds nothing and the
    /// viewer would show a rename as an addition.
    /// </summary>
    public string? PreviousPath { get; init; }

    public required ChangeStatus Status { get; init; }

    /// <summary>
    /// Lines added, or null when the number is not knowable. Null rather than zero, for the same
    /// reason <see cref="ChangedFile.LinesAdded"/> is: a box that reads <c>+0 −0</c> claims the
    /// file was touched and nothing changed, which is not what a binary file means.
    /// </summary>
    public int? LinesAdded { get; init; }

    /// <inheritdoc cref="LinesAdded"/>
    public int? LinesRemoved { get; init; }

    /// <summary>True when git treats the content as binary and will not produce a text diff.</summary>
    public bool IsBinary { get; init; }

    /// <summary>Detected language, or null when the filename is unrecognised.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// Project or module the file belongs to — what node fill colour encodes. The name alone; the
    /// manifest that produced the attribution is of no use once the run is over.
    /// </summary>
    public required string Project { get; init; }

    /// <summary>The facts worth keeping from one file of the changeset that was analysed.</summary>
    public static ChangedFileFacts From(ChangedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new ChangedFileFacts
        {
            Path = file.Path,
            PreviousPath = file.PreviousPath,
            Status = file.Status,
            LinesAdded = file.LinesAdded,
            LinesRemoved = file.LinesRemoved,
            IsBinary = file.IsBinary,
            Language = file.Language,
            Project = file.Project.Name,
        };
    }
}
