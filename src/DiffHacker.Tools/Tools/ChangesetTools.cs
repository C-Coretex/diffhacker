using System.ComponentModel;
using System.Globalization;
using DiffHacker.Core.Changes;
using ModelContextProtocol.Server;

namespace DiffHacker.Tools.Tools;

/// <summary>
/// The tools that answer "what changed".
/// <para>
/// Descriptions on these methods are prompt text, not API documentation. They are read by the
/// model on every turn, they are the difference between a tool being used well and being used
/// badly, and they are a deliverable of Iteration 5 in their own right. Each says what the tool
/// is for, what it will not do, and which tool to reach for instead.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class ChangesetTools(RepositorySession session, IGitClient git, ToolboxLimits limits)
{
    [McpServerTool(Name = "list_changed_files", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Lists every file that differs between the working tree and HEAD. This is the change under
        review, and its full extent — nothing is summarised away or hidden.

        Start here. Every other tool exists to explore around what this returns.

        Each row is: status (A added, M modified, D deleted, R renamed, C copied), lines added and
        removed, hunk count, detected language, the project or module the file belongs to, and the
        path. A dash means "not counted" — binary files and submodules have no line counts.

        Returns no file content. Use get_file_diff to see what changed inside a file, or read_file
        to see the whole file.

        Filters combine with AND. Large changesets are paged: if the result says it was truncated,
        call again with the cursor it gives you.
        """)]
    public async Task<string> ListChangedFilesAsync(
        [Description("Only files whose path matches this glob, e.g. 'src/**/*.ts'. Omit for all files.")]
        string? pathGlob = null,
        [Description("Only files of this detected language, e.g. 'typescript'. Case-insensitive.")]
        string? language = null,
        [Description("Only files in this project or module, as named in the project column.")]
        string? project = null,
        [Description("Only files with this status: one of 'added', 'modified', 'deleted', 'renamed', 'copied'.")]
        string? status = null,
        [Description("Re-read the repository before answering. Use only if you believe files changed since you started.")]
        bool refresh = false,
        [Description("Continuation token from a previous truncated result.")]
        string? cursor = null,
        [Description("Rows per page. Defaults to 150, capped at 500.")]
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (refresh)
        {
            await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        var changeset = session.Changeset;

        if (!changeset.HasCommits)
        {
            // Worth saying plainly. Against an empty repository every file looks added, and a
            // model that does not know why will invent a reason.
            return "This repository has no commits yet, so every file is reported as added.\n"
                + await RenderChangedAsync(pathGlob, language, project, status, cursor, limit).ConfigureAwait(false);
        }

        if (changeset.IsClean)
        {
            return "The working tree is clean: nothing differs from HEAD. There is no change to review.";
        }

        return await RenderChangedAsync(pathGlob, language, project, status, cursor, limit).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_file_diff", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Returns the unified diff for one or more changed files: exactly which lines were added and
        removed, with surrounding context.

        This is the tool for understanding what a change actually did. Ask for several related
        files in one call rather than one at a time.

        Only works for files that appear in list_changed_files. For an unchanged file, use
        read_file instead.

        Binary files report their size rather than a diff. Very large diffs are truncated per file
        and the whole result is capped; if you need all of a long diff, ask for that file alone.
        """)]
    public async Task<string> GetFileDiffAsync(
        [Description("Repository-relative paths, exactly as they appear in list_changed_files. At most 10 per call.")]
        string[] paths,
        CancellationToken cancellationToken = default)
    {
        if (paths is null || paths.Length == 0)
        {
            return "No paths were given. Pass one or more paths from list_changed_files.";
        }

        if (paths.Length > limits.DiffPathsPerCall)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Too many paths: {paths.Length}. Ask for at most {limits.DiffPathsPerCall} per call.");
        }

        var text = new ToolText(limits.MaxResultBytes);

        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"diffs for {paths.Length} file(s) · working tree vs HEAD · snapshot {ToolFormat.Timestamp(session.TakenAt)}");

        var budget = limits.DiffTotalBytes;

        foreach (var requested in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = session.Scope.ResolveFile(requested);
            if (!resolved.IsAccepted)
            {
                text.AddLine("--- " + requested + " ---");
                text.AddLine(resolved.Explain());
                continue;
            }

            var changed = session.FindChanged(resolved.RelativePath);
            if (changed is null)
            {
                text.AddLine("--- " + resolved.RelativePath + " ---");
                text.AddLine("This file did not change. Use read_file to see its contents.");
                continue;
            }

            budget -= await AppendDiffAsync(text, changed, budget, cancellationToken).ConfigureAwait(false);
        }

        return text.Render(header, text.HitByteCap
            ? "… truncated: the result reached its size cap. Ask for fewer files per call."
            : null);
    }

    [McpServerTool(Name = "get_path_info", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Describes paths without reading them: which project or module owns the path, its detected
        language, its size, and whether it is part of the change under review.

        Cheap. Use it to orient yourself before spending a read_file or a get_file_diff, and to
        find out why a path you expected is not readable — it distinguishes "does not exist" from
        "exists but is ignored by git", which no other tool does.
        """)]
    public async Task<string> GetPathInfoAsync(
        [Description("Repository-relative paths to describe. At most 50 per call.")]
        string[] paths,
        CancellationToken cancellationToken = default)
    {
        if (paths is null || paths.Length == 0)
        {
            return "No paths were given.";
        }

        if (paths.Length > limits.PathInfoPerCall)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Too many paths: {paths.Length}. Ask for at most {limits.PathInfoPerCall} per call.");
        }

        var text = new ToolText(limits.MaxResultBytes);

        foreach (var requested in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            text.AddLine(await DescribePathAsync(requested, cancellationToken).ConfigureAwait(false));
        }

        return text.Render(string.Create(
            CultureInfo.InvariantCulture,
            $"path info · snapshot {ToolFormat.Timestamp(session.TakenAt)}"));
    }

    private async Task<string> DescribePathAsync(string requested, CancellationToken cancellationToken)
    {
        var resolved = session.Scope.ResolveFile(requested);

        if (resolved.Rejection is PathRejection.NotVisible)
        {
            // The one place the toolbox distinguishes "ignored" from "absent". Telling a model
            // that node_modules/react does not exist would be a lie it would then reason from.
            var directory = session.Scope.ResolveDirectory(requested);

            if (directory.IsAccepted && (File.Exists(directory.AbsolutePath) || Directory.Exists(directory.AbsolutePath)))
            {
                var kind = Directory.Exists(directory.AbsolutePath) ? "directory" : "file";
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{requested}  exists as a {kind} but is ignored by git, so no tool can read it");
            }

            return requested + "  not found";
        }

        if (!resolved.IsAccepted)
        {
            return requested + "  " + resolved.Explain();
        }

        var path = resolved.RelativePath;
        var changed = session.FindChanged(path);

        var parts = new List<string>
        {
            "project=" + session.LocateProject(path).Name,
            "language=" + (LanguageTable.Detect(path) ?? "unknown"),
        };

        var content = await git
            .GetFileContentAsync(new FileContentQuery(session.Root, path, FileSide.WorkingTree), cancellationToken)
            .ConfigureAwait(false);

        parts.Add(content.Kind is FileContentKind.Absent
            ? "not in the working tree"
            : "size=" + ToolFormat.Bytes(content.SizeBytes));

        if (content.Kind is FileContentKind.Binary)
        {
            parts.Add("binary");
        }

        parts.Add(changed is null
            ? "unchanged"
            : string.Create(CultureInfo.InvariantCulture, $"changed={ToolFormat.Status(changed.Status)}"));

        return path + "  " + string.Join("  ", parts);
    }

    /// <summary>Appends one file's diff, and reports how much of the shared budget it spent.</summary>
    private async Task<int> AppendDiffAsync(
        ToolText text,
        ChangedFile changed,
        int budget,
        CancellationToken cancellationToken)
    {
        var heading = string.Create(
            CultureInfo.InvariantCulture,
            $"--- {changed.Path} ({ToolFormat.Status(changed.Status)}) ---");

        text.AddLine(heading);

        if (budget <= 0)
        {
            text.AddLine("Not shown: this call's diff budget is used up. Ask for this file on its own.");
            return 0;
        }

        var diff = await git.GetFileDiffAsync(
            new FileDiffQuery(session.Root, changed.Path, changed.PreviousPath, changed.IsUntracked),
            cancellationToken).ConfigureAwait(false);

        switch (diff.Kind)
        {
            case FileContentKind.Binary:
                text.AddLine("Binary file, " + ToolFormat.Bytes(diff.SizeBytes) + ". No textual diff exists.");
                return 0;

            case FileContentKind.TooLarge:
                text.AddLine("Diff is " + ToolFormat.Bytes(diff.SizeBytes) + ", too large to return. "
                    + "Use read_file with a line range to look at parts of it.");
                return 0;

            case FileContentKind.Absent:
                text.AddLine("No diff: git reports nothing changed for this path.");
                return 0;

            default:
                return AppendDiffBody(text, diff.UnifiedDiff ?? string.Empty, budget);
        }
    }

    private int AppendDiffBody(ToolText text, string unifiedDiff, int budget)
    {
        var allowance = Math.Min(budget, limits.DiffBytesPerFile);
        var spent = 0;
        var lines = 0;

        foreach (var line in unifiedDiff.Split('\n'))
        {
            var cost = line.Length + 1;

            if (spent + cost > allowance)
            {
                text.AddLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"… diff truncated after {lines} lines. Ask for this file alone, or use read_file with a line range."));

                return allowance;
            }

            if (!text.AddLine(line.TrimEnd('\r')))
            {
                return allowance;
            }

            spent += cost;
            lines++;
        }

        return spent;
    }

    private Task<string> RenderChangedAsync(
        string? pathGlob,
        string? language,
        string? project,
        string? status,
        string? cursor,
        int? limit)
    {
        var glob = Glob.TryCompile(pathGlob);

        if (pathGlob is not null && glob is null)
        {
            return Task.FromResult($"'{pathGlob}' is not a usable glob. Use patterns like 'src/**/*.ts'.");
        }

        var wanted = ParseStatus(status);
        if (status is not null && wanted is null)
        {
            return Task.FromResult(
                $"'{status}' is not a status. Use one of: added, modified, deleted, renamed, copied.");
        }

        var matched = session.Changeset.Files
            .Where(file => Glob.Matches(glob, file.Path))
            .Where(file => language is null
                || string.Equals(file.Language, language, StringComparison.OrdinalIgnoreCase))
            .Where(file => project is null
                || string.Equals(file.Project.Name, project, StringComparison.OrdinalIgnoreCase))
            .Where(file => wanted is null || file.Status == wanted)
            .ToArray();

        var pageSize = ToolboxLimits.Clamp(limit, limits.ChangedFilesPageSize, limits.ChangedFilesMaxPageSize);
        var fingerprint = Continuation.Describe(pathGlob, language, project, status, matched.Length);

        var offset = 0;
        if (cursor is not null && !Continuation.TryDecode(cursor, "list_changed_files", fingerprint, out offset))
        {
            return Task.FromResult(Continuation.Mismatch);
        }

        if (matched.Length == 0)
        {
            return Task.FromResult(
                "No changed file matches those filters. Call list_changed_files with no filters to see the whole change.");
        }

        var statistics = session.Changeset.Statistics;

        var text = new ToolText(limits.MaxResultBytes);
        var shown = 0;

        foreach (var file in matched.Skip(offset))
        {
            if (shown >= pageSize || !text.AddLine(ToolFormat.ChangedRow(file)))
            {
                break;
            }

            shown++;
        }

        var delivered = offset + shown;

        // Built now rather than up front: "changed files 1-150" must not sit above a footer
        // saying 12 because the byte cap stopped the body early.
        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"changed files {offset + 1}-{delivered} of {matched.Length}"
            + $" · whole change: {statistics.TotalFiles} files, +{statistics.TotalLinesAdded} -{statistics.TotalLinesRemoved}"
            + $" · snapshot {ToolFormat.Timestamp(session.TakenAt)}\n{ToolFormat.ChangedRowLegend}");

        return Task.FromResult(text.Render(header, delivered < matched.Length
            ? ToolText.TruncationFooter(
                shown,
                matched.Length,
                totalIsExact: true,
                "files",
                Continuation.Encode("list_changed_files", fingerprint, delivered))
            : null));
    }

    private static ChangeStatus? ParseStatus(string? status) => status?.ToUpperInvariant() switch
    {
        null => null,
        "ADDED" or "A" => ChangeStatus.Added,
        "MODIFIED" or "M" => ChangeStatus.Modified,
        "DELETED" or "D" => ChangeStatus.Deleted,
        "RENAMED" or "R" => ChangeStatus.Renamed,
        "COPIED" or "C" => ChangeStatus.Copied,
        _ => null,
    };
}
