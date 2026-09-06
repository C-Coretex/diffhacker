using System.Globalization;

namespace DiffHacker.Tools;

/// <summary>
/// Directories, derived from the flat list of files git can see.
/// <para>
/// Git does not track directories, so there is no directory listing to ask it for. Building them
/// from the path list rather than from the filesystem is what makes the toolbox's view of the
/// repository consistent: exactly one definition of what is visible, used by every tool, with no
/// second code path that could disagree about <c>node_modules</c>.
/// </para>
/// <para>
/// The visible consequence is that an empty directory does not appear. Git does not track those
/// either, so nothing is lost that git itself would have shown.
/// </para>
/// </summary>
internal sealed class DirectoryView
{
    private DirectoryView(string displayName, IReadOnlyList<DirectoryEntry> entries)
    {
        DisplayName = displayName;
        Entries = entries;
    }

    public string DisplayName { get; }

    public IReadOnlyList<DirectoryEntry> Entries { get; }

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>The immediate children of one directory.</summary>
    public static DirectoryView Of(RepositorySession session, string relativeDirectory)
    {
        var prefix = relativeDirectory.Length == 0 ? string.Empty : relativeDirectory + "/";
        var display = relativeDirectory.Length == 0 ? "repository root" : relativeDirectory + "/";

        var directories = new Dictionary<string, (int Files, int Changed)>(StringComparer.Ordinal);
        var files = new List<DirectoryEntry>();

        foreach (var path in session.VisibleFiles)
        {
            if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = path[prefix.Length..];
            var separator = remainder.IndexOf('/', StringComparison.Ordinal);

            if (separator < 0)
            {
                files.Add(DirectoryEntry.File(remainder, session.FindChanged(path)));
                continue;
            }

            var child = remainder[..separator];
            var counts = directories.GetValueOrDefault(child);

            directories[child] = (
                counts.Files + 1,
                counts.Changed + (session.FindChanged(path) is null ? 0 : 1));
        }

        var entries = directories
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => DirectoryEntry.Directory(pair.Key, pair.Value.Files, pair.Value.Changed))
            .Concat(files.OrderBy(entry => entry.Name, StringComparer.Ordinal))
            .ToArray();

        return new DirectoryView(display, entries);
    }

    /// <summary>
    /// An indented tree, breadth-limited by <paramref name="maxDepth"/>. Directories that were
    /// not descended into say how many files they hold, so the shape stays honest at the edge.
    /// </summary>
    public static List<string> Tree(RepositorySession session, string relativeDirectory, int maxDepth)
    {
        var rows = new List<string>();
        Descend(session, relativeDirectory, 0, maxDepth, rows);
        return rows;
    }

    private static void Descend(
        RepositorySession session,
        string relativeDirectory,
        int depth,
        int maxDepth,
        List<string> rows)
    {
        var view = Of(session, relativeDirectory);
        var indent = new string(' ', depth * 2);

        foreach (var entry in view.Entries)
        {
            if (!entry.IsDirectory)
            {
                rows.Add(indent + entry.Name + (entry.Status is { } status ? "  " + status : string.Empty));
                continue;
            }

            var changed = entry.ChangedCount > 0
                ? string.Create(CultureInfo.InvariantCulture, $", {entry.ChangedCount} changed")
                : string.Empty;

            rows.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{indent}{entry.Name}/  ({entry.FileCount} files{changed})"));

            if (depth + 1 >= maxDepth)
            {
                continue;
            }

            var child = relativeDirectory.Length == 0 ? entry.Name : relativeDirectory + "/" + entry.Name;
            Descend(session, child, depth + 1, maxDepth, rows);
        }
    }
}

/// <summary>One row of a directory listing.</summary>
internal readonly record struct DirectoryEntry
{
    public required string Name { get; init; }

    public required bool IsDirectory { get; init; }

    public int FileCount { get; init; }

    public int ChangedCount { get; init; }

    /// <summary>The file's change marker, or null when it did not change.</summary>
    public string? Status { get; init; }

    public static DirectoryEntry Directory(string name, int fileCount, int changedCount) =>
        new()
        {
            Name = name,
            IsDirectory = true,
            FileCount = fileCount,
            ChangedCount = changedCount,
        };

    public static DirectoryEntry File(string name, Core.Changes.ChangedFile? changed) =>
        new()
        {
            Name = name,
            IsDirectory = false,
            Status = changed is null ? null : "[" + ToolFormat.Status(changed.Status) + "]",
        };
}
