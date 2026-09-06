using System.Globalization;
using DiffHacker.Core.Changes;

namespace DiffHacker.Tools;

/// <summary>
/// How the toolbox spells things, so nine tools spell them the same way.
/// </summary>
internal static class ToolFormat
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

    /// <summary>The snapshot stamp every result header carries, so a stale answer is a legible one.</summary>
    public static string Timestamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Sizes a person would read, because a model reads them the same way.</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        if (bytes < 1024 * 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB");
    }

    /// <summary>One row of <c>list_changed_files</c>. Path last, so an odd path cannot shift a column.</summary>
    public static string ChangedRow(ChangedFile file)
    {
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

        return row;
    }

    /// <summary>The column key printed above a changed-file listing.</summary>
    public const string ChangedRowLegend =
        "columns: status  +added  -removed  hunks  language  project  path  [flags]";

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
