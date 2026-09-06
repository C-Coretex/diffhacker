using DiffHacker.Core.Changes;

namespace DiffHacker.Tools;

/// <summary>
/// One repository, as the toolbox sees it for the length of a session.
/// <para>
/// The changeset and the visible-file list are taken once and reused. That is both an economy — a
/// 1500-file changeset costs four git passes, and paying that per tool call would dominate the
/// run — and a correctness property: a model reasoning across twenty calls should not have the
/// file list move underneath it.
/// </para>
/// <para>
/// It can still be re-taken deliberately, because the stdio server has a use the in-app path does
/// not: a developer editing files while an agent explores them. <c>list_changed_files</c> exposes
/// that as <c>refresh</c>, and every result header carries the snapshot's timestamp so a stale
/// answer is at least a legible one.
/// </para>
/// </summary>
public sealed class RepositorySession
{
    private readonly IGitClient _git;
    private readonly Lock _gate = new();
    private Snapshot _snapshot;

    private RepositorySession(IGitClient git, string root, Snapshot snapshot)
    {
        _git = git;
        Root = root;
        _snapshot = snapshot;
    }

    /// <summary>The worktree root, absolute.</summary>
    public string Root { get; }

    public Changeset Changeset => Current.Changeset;

    /// <summary>Every file git can see, sorted, repository-relative.</summary>
    public IReadOnlyList<string> VisibleFiles => Current.VisibleFiles;

    public RepositoryScope Scope => Current.Scope;

    public DateTimeOffset TakenAt => Current.TakenAt;

    private Snapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public static async Task<RepositorySession> CreateAsync(
        IGitClient git,
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var snapshot = await TakeAsync(git, repositoryPath, cancellationToken).ConfigureAwait(false);
        return new RepositorySession(git, snapshot.Changeset.RepositoryPath, snapshot);
    }

    /// <summary>Re-reads the repository. Returns the new snapshot's timestamp.</summary>
    public async Task<DateTimeOffset> RefreshAsync(CancellationToken cancellationToken)
    {
        var snapshot = await TakeAsync(_git, Root, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _snapshot = snapshot;
        }

        return snapshot.TakenAt;
    }

    /// <summary>
    /// The changed file at <paramref name="relativePath"/>, or null if it did not change.
    /// </summary>
    public ChangedFile? FindChanged(string relativePath)
    {
        var snapshot = Current;
        return snapshot.Changed.GetValueOrDefault(relativePath);
    }

    /// <summary>
    /// Which project or module a path belongs to.
    /// <para>
    /// <see cref="ProjectLocator"/> caches per directory and documents itself as one instance per
    /// run and not thread-safe. Tools inside a single turn are dispatched concurrently, so the
    /// lock is not optional.
    /// </para>
    /// </summary>
    public ProjectReference LocateProject(string relativePath)
    {
        var snapshot = Current;

        lock (snapshot.LocatorGate)
        {
            return snapshot.Locator.Locate(relativePath);
        }
    }

    private static async Task<Snapshot> TakeAsync(
        IGitClient git,
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var changeset = await git
            .GetChangesetAsync(new ChangesetQuery(repositoryPath), cancellationToken)
            .ConfigureAwait(false);

        var visible = await git
            .ListFilesAsync(new FileListQuery(changeset.RepositoryPath), cancellationToken)
            .ConfigureAwait(false);

        var visibleSet = new HashSet<string>(visible, StringComparer.Ordinal);

        // A deleted file is in the changeset but not on disk, so ls-files does not report it.
        // Leaving it out would make get_file_diff refuse the very files a reviewer most wants to
        // ask about, so the changeset's own paths join the visible set.
        foreach (var file in changeset.Files)
        {
            visibleSet.Add(file.Path);

            if (file.PreviousPath is { } previous)
            {
                visibleSet.Add(previous);
            }
        }

        return new Snapshot
        {
            Changeset = changeset,
            VisibleFiles = visible,
            Changed = changeset.Files.ToDictionary(file => file.Path, StringComparer.Ordinal),
            Scope = new RepositoryScope(changeset.RepositoryPath, visibleSet),
            Locator = new ProjectLocator(changeset.RepositoryPath),
            LocatorGate = new Lock(),
            TakenAt = DateTimeOffset.UtcNow,
        };
    }

    private sealed record Snapshot
    {
        public required Changeset Changeset { get; init; }

        public required IReadOnlyList<string> VisibleFiles { get; init; }

        public required IReadOnlyDictionary<string, ChangedFile> Changed { get; init; }

        public required RepositoryScope Scope { get; init; }

        public required ProjectLocator Locator { get; init; }

        public required Lock LocatorGate { get; init; }

        public required DateTimeOffset TakenAt { get; init; }
    }
}
