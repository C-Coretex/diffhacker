using System.ComponentModel;
using System.Globalization;
using DiffHacker.Core.Changes;
using ModelContextProtocol.Server;

namespace DiffHacker.Tools.Tools;

/// <summary>
/// The tools that answer "what is here" and "what does this file say".
/// <para>
/// Every one of them works from the visible set — tracked files plus untracked files git does not
/// ignore. That is what keeps <c>node_modules</c>, <c>dist</c> and <c>obj</c> out of the model's
/// context without reimplementing <c>.gitignore</c>. Ignored entries are counted rather than
/// concealed, so a directory listing never implies a folder is empty when it is merely expensive.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class FileTools(RepositorySession session, IGitClient git, ToolboxLimits limits)
{
    [McpServerTool(Name = "read_file", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Reads a file, or a range of lines from it, with line numbers.

        Two sides are available. 'working_tree' is the file as it is now, including uncommitted
        changes — this is what you usually want. 'head' is the committed version, useful for
        seeing what a changed file looked like before.

        Reads are paged by line. The header tells you the file's total line count, so you can ask
        for the next range directly rather than guessing. Prefer a range around something you
        found with search_text over reading a whole large file.

        Binary files and files over 5 MB are reported, not returned. Files that are not UTF-8 are
        decoded and the encoding is named in the header, so you are never handed silent mojibake.
        """)]
    public async Task<string> ReadFileAsync(
        [Description("Repository-relative path, e.g. 'src/app/main.ts'.")]
        string path,
        [Description("Which side to read: 'working_tree' (default) or 'head'.")]
        string side = "working_tree",
        [Description("First line to return, 1-based. Defaults to 1.")]
        int? startLine = null,
        [Description("How many lines to return. Defaults to 400, capped at 2000.")]
        int? lineCount = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = session.Scope.ResolveFile(path);
        if (!resolved.IsAccepted)
        {
            return resolved.Explain();
        }

        var wantsHead = side.Equals("head", StringComparison.OrdinalIgnoreCase);

        var content = await git.GetFileContentAsync(
            new FileContentQuery(session.Root, resolved.RelativePath, wantsHead ? FileSide.Head : FileSide.WorkingTree),
            cancellationToken).ConfigureAwait(false);

        var sideName = wantsHead ? "HEAD" : "working tree";

        switch (content.Kind)
        {
            case FileContentKind.Absent:
                var hint = wantsHead
                    ? " It is probably a newly added file — read the working tree side instead."
                    : string.Empty;

                return $"{resolved.RelativePath} does not exist on the {sideName} side." + hint;

            case FileContentKind.Binary:
                return $"{resolved.RelativePath} is a binary file, {ToolFormat.Bytes(content.SizeBytes)}. "
                    + "There is nothing to read as text.";

            case FileContentKind.TooLarge:
                return $"{resolved.RelativePath} is {ToolFormat.Bytes(content.SizeBytes)}, larger than the 5 MB "
                    + "this toolbox will read. Use search_text to find what you need inside it.";
        }

        var lines = SplitLines(content.Text ?? string.Empty);
        var first = Math.Max(startLine ?? 1, 1);
        var take = ToolboxLimits.Clamp(lineCount, limits.ReadFileLines, limits.ReadFileMaxLines);

        if (first > lines.Count)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{resolved.RelativePath} has {lines.Count} lines, so line {first} is past the end.");
        }

        var last = Math.Min(first + take - 1, lines.Count);

        var encoding = content.Encoding ?? "utf-8";
        var fallbackNote = content.UsedFallbackEncoding
            ? " (not valid UTF-8; decoded as Latin-1, so some characters may not be what the author typed)"
            : string.Empty;

        var text = new ToolText(Math.Min(limits.MaxResultBytes, limits.ReadFileBytes + 512));

        var shown = 0;

        for (var number = first; number <= last; number++)
        {
            if (!text.AddLine(string.Create(CultureInfo.InvariantCulture, $"{number,6}  {lines[number - 1]}")))
            {
                break;
            }

            shown++;
        }

        var reached = first + shown - 1;

        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"{resolved.RelativePath} · {sideName} · lines {first}-{reached} of {lines.Count} · {encoding}{fallbackNote}");

        return text.Render(header, reached < lines.Count
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"… truncated: showing lines {first}-{reached} of {lines.Count}. Call again with startLine={reached + 1}.")
            : null);
    }

    [McpServerTool(Name = "find_files", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Finds files by path pattern. Use '*' to match within a path segment, '**' to cross
        directories: 'src/**/*.test.ts', '**/Dockerfile', 'docs/*.md'.

        This is how you locate a file whose exact path you do not know. It searches names only —
        use search_text to search inside files.

        Only files git can see are listed. Anything covered by .gitignore is invisible to every
        tool here, so a pattern that should obviously match may return nothing for that reason;
        get_path_info on the path will tell you if that is what happened.
        """)]
    public string FindFiles(
        [Description("Glob to match against repository-relative paths.")]
        string glob,
        [Description("Only files that are part of the change under review.")]
        bool changedOnly = false,
        [Description("Continuation token from a previous truncated result.")]
        string? cursor = null,
        [Description("Paths per page. Defaults to 200, capped at 1000.")]
        int? limit = null)
    {
        var compiled = Glob.TryCompile(glob);
        if (compiled is null)
        {
            return $"'{glob}' is not a usable glob. Use patterns like 'src/**/*.ts' or '**/Dockerfile'.";
        }

        var candidates = changedOnly
            ? session.Changeset.Files.Select(file => file.Path).Order(StringComparer.Ordinal)
            : (IEnumerable<string>)session.VisibleFiles;

        var matched = candidates.Where(path => Glob.Matches(compiled, path)).ToArray();

        if (matched.Length == 0)
        {
            return changedOnly
                ? $"No changed file matches '{glob}'. Try again without changedOnly to search the whole repository."
                : $"No file matches '{glob}'.";
        }

        var pageSize = ToolboxLimits.Clamp(limit, limits.FindFilesPageSize, limits.FindFilesMaxPageSize);
        var fingerprint = Continuation.Describe(glob, changedOnly, matched.Length);

        var offset = 0;
        if (cursor is not null && !Continuation.TryDecode(cursor, "find_files", fingerprint, out offset))
        {
            return Continuation.Mismatch;
        }

        var scope = changedOnly ? "changed files" : "all visible files";

        var text = new ToolText(limits.MaxResultBytes);

        // The header states the total, which is true whatever the body managed to hold; the
        // footer states what was actually shown.
        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"{matched.Length} path(s) matching '{glob}' in {scope} · snapshot {ToolFormat.Timestamp(session.TakenAt)}");

        var shown = 0;

        foreach (var path in matched.Skip(offset))
        {
            if (shown >= pageSize || !text.AddLine(path))
            {
                break;
            }

            shown++;
        }

        var delivered = offset + shown;

        return text.Render(header, delivered < matched.Length
            ? ToolText.TruncationFooter(
                shown,
                matched.Length,
                totalIsExact: true,
                "paths",
                Continuation.Encode("find_files", fingerprint, delivered))
            : null);
    }

    [McpServerTool(Name = "list_directory", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Lists one directory: its subdirectories, with how many files each contains, and its files,
        with their sizes and whether they changed.

        Use it to orient yourself in an unfamiliar repository. get_repository_tree is better for
        seeing shape at a glance; this is better for looking at one place closely.

        Directories covered by .gitignore are not listed, but the count of what was hidden is —
        so an apparently sparse directory never quietly misleads you.
        """)]
    public string ListDirectory(
        [Description("Repository-relative directory path. Omit or pass '' for the repository root.")]
        string? path = null,
        [Description("Continuation token from a previous truncated result.")]
        string? cursor = null,
        [Description("Entries per page. Defaults to 300.")]
        int? limit = null)
    {
        var resolved = session.Scope.ResolveDirectory(path);
        if (!resolved.IsAccepted)
        {
            return resolved.Explain();
        }

        var listing = DirectoryView.Of(session, resolved.RelativePath);

        if (listing.IsEmpty)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{listing.DisplayName} contains no files git can see.{HiddenSuffix(resolved.AbsolutePath, listing)}");
        }

        var entries = listing.Entries;
        var pageSize = ToolboxLimits.Clamp(limit, limits.DirectoryPageSize, limits.DirectoryPageSize);
        var fingerprint = Continuation.Describe(resolved.RelativePath, entries.Count);

        var offset = 0;
        if (cursor is not null && !Continuation.TryDecode(cursor, "list_directory", fingerprint, out offset))
        {
            return Continuation.Mismatch;
        }

        var text = new ToolText(limits.MaxResultBytes);

        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"{listing.DisplayName} · {entries.Count} entries · snapshot {ToolFormat.Timestamp(session.TakenAt)}");

        var shown = 0;

        foreach (var entry in entries.Skip(offset))
        {
            if (shown >= pageSize || !text.AddLine(Describe(entry)))
            {
                break;
            }

            shown++;
        }

        var delivered = offset + shown;
        var hidden = HiddenSuffix(resolved.AbsolutePath, listing);

        if (delivered < entries.Count)
        {
            return text.Render(header, ToolText.TruncationFooter(
                shown,
                entries.Count,
                totalIsExact: true,
                "entries",
                Continuation.Encode("list_directory", fingerprint, delivered)) + hidden);
        }

        return text.Render(header, hidden.Length > 0 ? hidden.TrimStart() : null);
    }

    [McpServerTool(Name = "get_repository_tree", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Shows the directory structure as an indented tree, with the number of files in each
        directory and how many of them changed.

        The fastest way to understand how a repository is laid out. Start at the root with the
        default depth to see the shape, then pass a path and a greater depth to go into whichever
        part matters.

        Only shows what git can see. Depth is limited and the result is capped, so a very wide
        repository returns its top and tells you what it left out.
        """)]
    public string GetRepositoryTree(
        [Description("Repository-relative directory to start from. Omit for the repository root.")]
        string? path = null,
        [Description("How many levels deep to descend. Defaults to 2, capped at 10.")]
        int? maxDepth = null,
        [Description("Continuation token from a previous truncated result.")]
        string? cursor = null,
        [Description("Entries per page. Defaults to 500.")]
        int? limit = null)
    {
        var resolved = session.Scope.ResolveDirectory(path);
        if (!resolved.IsAccepted)
        {
            return resolved.Explain();
        }

        var depth = ToolboxLimits.Clamp(maxDepth, limits.TreeDepth, limits.TreeMaxDepth);
        var rows = DirectoryView.Tree(session, resolved.RelativePath, depth);

        if (rows.Count == 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Nothing under {(resolved.RelativePath.Length == 0 ? "the repository root" : resolved.RelativePath)} is visible to git.");
        }

        var pageSize = ToolboxLimits.Clamp(limit, limits.TreeEntries, limits.TreeEntries);
        var fingerprint = Continuation.Describe(resolved.RelativePath, depth, rows.Count);

        var offset = 0;
        if (cursor is not null && !Continuation.TryDecode(cursor, "get_repository_tree", fingerprint, out offset))
        {
            return Continuation.Mismatch;
        }

        var root = resolved.RelativePath.Length == 0 ? "/" : resolved.RelativePath + "/";

        var text = new ToolText(limits.MaxResultBytes);

        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"tree of {root} to depth {depth} · {rows.Count} entries · snapshot {ToolFormat.Timestamp(session.TakenAt)}");

        var shown = 0;

        foreach (var row in rows.Skip(offset))
        {
            if (shown >= pageSize || !text.AddLine(row))
            {
                break;
            }

            shown++;
        }

        var delivered = offset + shown;

        return text.Render(header, delivered < rows.Count
            ? ToolText.TruncationFooter(
                shown,
                rows.Count,
                totalIsExact: true,
                "entries",
                Continuation.Encode("get_repository_tree", fingerprint, delivered))
            : null);
    }

    private static string Describe(DirectoryEntry entry)
    {
        if (entry.IsDirectory)
        {
            var changed = entry.ChangedCount > 0
                ? string.Create(CultureInfo.InvariantCulture, $", {entry.ChangedCount} changed")
                : string.Empty;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{entry.Name}/  ({entry.FileCount} files{changed})");
        }

        var status = entry.Status is { } s ? "  " + s : string.Empty;
        return entry.Name + status;
    }

    /// <summary>
    /// How many entries on disk the visible set does not include.
    /// <para>
    /// This is the whole of the "counted, not concealed" promise. It reads the real directory —
    /// the only filesystem enumeration in the toolbox — and reports the difference rather than
    /// letting a model conclude that <c>node_modules</c> does not exist.
    /// </para>
    /// </summary>
    private static string HiddenSuffix(string absoluteDirectory, DirectoryView listing)
    {
        int actual;

        try
        {
            actual = Directory.EnumerateFileSystemEntries(absoluteDirectory)
                .Count(entry => !Path.GetFileName(entry).Equals(".git", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }

        var hidden = actual - listing.Entries.Count;

        return hidden <= 0
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"\n{hidden} more entr{(hidden == 1 ? "y is" : "ies are")} present but ignored by git, so no tool here can read them.");
    }

    /// <summary>Splits on newlines, keeping the file's own line count exactly.</summary>
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();

        if (text.Length == 0)
        {
            return lines;
        }

        var start = 0;

        while (start <= text.Length)
        {
            var newline = text.IndexOf('\n', start);

            if (newline < 0)
            {
                if (start < text.Length)
                {
                    lines.Add(text[start..].TrimEnd('\r'));
                }

                break;
            }

            lines.Add(text[start..newline].TrimEnd('\r'));
            start = newline + 1;
        }

        return lines;
    }
}
