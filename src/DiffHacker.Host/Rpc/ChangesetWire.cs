using DiffHacker.Contracts;
using DiffHacker.Core.Changes;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Translates the changeset domain onto the wire contract, and back.
/// <para>
/// The two sides are separate on purpose. The generated types come from
/// <c>/schema</c> and are the agreement with the renderer; the domain types are what
/// <c>DiffHacker.Core</c> reasons about. Mapping them here, with exhaustive switches that throw
/// on an unmapped value, means a new status added on one side cannot quietly become the wrong
/// status on the other — the same reason <see cref="ProviderTypeWire"/> exists.
/// </para>
/// </summary>
public static class ChangesetWire
{
    public static ChangesetResult ToWire(Changeset changeset)
    {
        ArgumentNullException.ThrowIfNull(changeset);

        var files = new List<ChangedFileInfo>(changeset.Files.Count);
        foreach (var file in changeset.Files)
        {
            files.Add(ToWire(file));
        }

        return new ChangesetResult(
            files: files.AsReadOnly(),
            hasCommits: changeset.HasCommits,
            hunkCountsAvailable: changeset.HunkCountsAvailable,
            isClean: changeset.IsClean,
            repositoryPath: changeset.RepositoryPath,
            statistics: ToWire(changeset.Statistics),
            untrackedIncluded: changeset.UntrackedIncluded);
    }

    public static FileDiffInfo ToWire(FileDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        return new FileDiffInfo(
            kind: ToDiffKind(diff.Kind),
            path: diff.Path,
            previousPath: diff.PreviousPath,
            sizeBytes: diff.SizeBytes,
            unifiedDiff: diff.UnifiedDiff);
    }

    public static FileContentInfo ToWire(FileContentResult content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new FileContentInfo(
            encoding: content.Encoding,
            kind: ToContentKind(content.Kind),
            sizeBytes: content.SizeBytes,
            text: content.Text,
            usedFallbackEncoding: content.UsedFallbackEncoding);
    }

    public static FileSide FromWire(FileContentRequestSide side) => side switch
    {
        FileContentRequestSide.Head => FileSide.Head,
        FileContentRequestSide.Working_tree => FileSide.WorkingTree,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unmapped file side."),
    };

    private static ChangedFileInfo ToWire(ChangedFile file) =>
        new(
            hunkCount: file.HunkCount,
            isBinary: file.IsBinary,
            isNestedRepository: file.IsNestedRepository,
            isSubmodule: file.IsSubmodule,
            isSymlink: file.IsSymlink,
            isUntracked: file.IsUntracked,
            language: file.Language,
            linesAdded: file.LinesAdded,
            linesRemoved: file.LinesRemoved,
            path: file.Path,
            previousPath: file.PreviousPath,
            project: file.Project.Name,
            projectManifest: file.Project.Manifest,
            status: ToWire(file.Status),
            submoduleFromCommit: file.SubmoduleFromCommit,
            submoduleToCommit: file.SubmoduleToCommit);

    private static ChangesetStats ToWire(ChangesetStatistics statistics) =>
        new(
            addedFiles: statistics.ByStatus.Added,
            binaryFiles: statistics.BinaryFiles,
            copiedFiles: statistics.ByStatus.Copied,
            deletedFiles: statistics.ByStatus.Deleted,
            languages: statistics.Languages,
            modifiedFiles: statistics.ByStatus.Modified,
            projects: statistics.Projects,
            renamedFiles: statistics.ByStatus.Renamed,
            submoduleFiles: statistics.SubmoduleFiles,
            totalFiles: statistics.TotalFiles,
            totalLinesAdded: statistics.TotalLinesAdded,
            totalLinesRemoved: statistics.TotalLinesRemoved,
            untrackedFiles: statistics.UntrackedFiles);

    public static ChangedFileInfoStatus ToWire(ChangeStatus status) => status switch
    {
        ChangeStatus.Added => ChangedFileInfoStatus.Added,
        ChangeStatus.Modified => ChangedFileInfoStatus.Modified,
        ChangeStatus.Deleted => ChangedFileInfoStatus.Deleted,
        ChangeStatus.Renamed => ChangedFileInfoStatus.Renamed,
        ChangeStatus.Copied => ChangedFileInfoStatus.Copied,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped change status."),
    };

    public static FileDiffInfoKind ToDiffKind(FileContentKind kind) => kind switch
    {
        FileContentKind.Text => FileDiffInfoKind.Text,
        FileContentKind.Binary => FileDiffInfoKind.Binary,
        FileContentKind.Absent => FileDiffInfoKind.Absent,
        FileContentKind.TooLarge => FileDiffInfoKind.Too_large,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped diff kind."),
    };

    public static FileContentInfoKind ToContentKind(FileContentKind kind) => kind switch
    {
        FileContentKind.Text => FileContentInfoKind.Text,
        FileContentKind.Binary => FileContentInfoKind.Binary,
        FileContentKind.Absent => FileContentInfoKind.Absent,
        FileContentKind.TooLarge => FileContentInfoKind.Too_large,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped content kind."),
    };
}
