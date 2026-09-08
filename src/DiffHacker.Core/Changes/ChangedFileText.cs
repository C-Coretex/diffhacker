using System.Globalization;

namespace DiffHacker.Core.Changes;

/// <summary>
/// How a changed file is spelled to a model.
/// <para>
/// It lives in Core rather than in the toolbox because it now has two callers that must not
/// disagree: <c>list_changed_files</c>, which the model reads when it asks what changed, and the
/// opening message of an analysis run, which tells it what changed before it asks anything. The
/// same file described two ways in one conversation is a model's problem to reconcile and ours to
/// have caused, so there is one renderer and the toolbox delegates to it.
/// </para>
/// </summary>
public static class ChangedFileText
{
    /// <summary>
    /// Git's own one-letter status codes. A model has seen millions of lines of
    /// <c>git status --short</c>; there is nothing to gain by inventing new words for these.
    /// </summary>
    public static string Status(ChangeStatus status) => status switch
    {
        ChangeStatus.Added => "A",
        ChangeStatus.Modified => "M",
        ChangeStatus.Deleted => "D",
        ChangeStatus.Renamed => "R",
        ChangeStatus.Copied => "C",
        _ => "?",
    };

    /// <summary>One row of a changed-file listing. Path last, so an odd path cannot shift a column.</summary>
    /// <param name="file">The changed file.</param>
    /// <param name="withheld">
    /// Whether its content is withheld. Flagged rather than omitted: the file really did change,
    /// and a reviewer who cannot see that it changed is worse off than one who can see it changed
    /// but not how.
    /// </param>
    public static string Row(ChangedFile file, bool withheld = false)
    {
        ArgumentNullException.ThrowIfNull(file);

        var added = file.LinesAdded is { } a
            ? string.Create(CultureInfo.InvariantCulture, $"+{a}")
            : "+-";

        var removed = file.LinesRemoved is { } r
            ? string.Create(CultureInfo.InvariantCulture, $"-{r}")
            : "--";

        var hunks = file.HunkCount is { } h
            ? string.Create(CultureInfo.InvariantCulture, $"{h}h")
            : "-h";

        var row = string.Create(
            CultureInfo.InvariantCulture,
            $"{Status(file.Status)} {added} {removed} {hunks} {file.Language ?? "-"} {file.Project.Name} {file.Path}");

        if (file.PreviousPath is { } previous)
        {
            row += string.Create(CultureInfo.InvariantCulture, $" (was {previous})");
        }

        foreach (var flag in Flags(file))
        {
            row += " [" + flag + "]";
        }

        if (withheld)
        {
            row += " [content withheld]";
        }

        return row;
    }

    /// <summary>The column key printed above a changed-file listing.</summary>
    public const string Legend =
        "columns: status  +added  -removed  hunks  language  project  path  [flags]\n"
        + "[content withheld] means the file changed but may hold credentials, so it cannot be read or diffed.";

    private static IEnumerable<string> Flags(ChangedFile file)
    {
        if (file.IsBinary)
        {
            yield return "binary";
        }

        if (file.IsUntracked)
        {
            yield return "untracked";
        }

        if (file.IsSubmodule)
        {
            yield return "submodule";
        }

        if (file.IsSymlink)
        {
            yield return "symlink";
        }

        if (file.IsNestedRepository)
        {
            yield return "nested repository";
        }
    }
}
