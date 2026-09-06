using DiffHacker.Core.Changes;
using DiffHacker.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Git;

/// <summary>
/// Produces the changeset — the working tree against <c>HEAD</c> — from the git command line.
/// <para>
/// Four streaming passes build the file list, and none of them holds a file's content:
/// <c>--raw</c> for paths, statuses and modes; <c>--numstat</c> for line counts; a <c>-U0</c>
/// patch scan for hunk counts; and <c>ls-files --others</c> for untracked files. Content and
/// diffs are fetched one file at a time, on demand, which is how requirement 2 and requirement 8
/// coexist.
/// </para>
/// <para>
/// Every invocation goes through <see cref="GitProcessRunner"/>, so the read-only allowlist
/// applies to all of it (§0.2.12), and every diff passes <c>--no-ext-diff --no-textconv</c> so a
/// repository's own configuration cannot turn a read into arbitrary command execution.
/// </para>
/// </summary>
public sealed partial class GitClient(
    GitProcessRunner runner,
    IGitEnvironment environment,
    ILogger<GitClient> logger)
    : IGitClient
{
    /// <summary>
    /// The empty tree, so a repository with no commits still has something to compare against.
    /// Well-known constants rather than <c>git hash-object</c>, which can write to the object
    /// database and therefore has no business on the allowlist.
    /// </summary>
    private const string EmptyTreeSha1 = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    private const string EmptyTreeSha256 =
        "6ef19b41225c5369f1c104d45d8d85efa9b057b53b14b4b9b939dd74decc5321";

    /// <summary>
    /// Applied to every diff. <c>-M</c> and <c>-C</c> ask for rename and copy detection
    /// explicitly rather than inheriting whatever the user's config says, so the same working
    /// tree produces the same changeset on every machine.
    /// </summary>
    private static readonly string[] CommonDiffOptions =
    [
        "--no-color",
        "--no-ext-diff",
        "--no-textconv",
        "--ignore-submodules=none",
        "-M",
        "-C",
    ];

    public async Task<Changeset> GetChangesetAsync(ChangesetQuery query, CancellationToken cancellationToken)
    {
        var root = await RequireRepositoryAsync(query.RepositoryPath, cancellationToken).ConfigureAwait(false);

        var baseRevision = await ResolveBaseRevisionAsync(root, cancellationToken).ConfigureAwait(false);

        var raw = await ReadRawEntriesAsync(root, baseRevision.Revision, cancellationToken).ConfigureAwait(false);
        var numstat = await ReadNumstatAsync(root, baseRevision.Revision, cancellationToken).ConfigureAwait(false);
        var hunks = await ReadHunkCountsAsync(root, baseRevision.Revision, raw.Count, cancellationToken)
            .ConfigureAwait(false);

        var locator = new ProjectLocator(root);
        var files = new List<ChangedFile>(raw.Count);

        for (var index = 0; index < raw.Count; index++)
        {
            var entry = raw[index];
            var measured = numstat.TryGetValue(entry.Path, out var stats);

            // A submodule's "1 added, 1 removed" is git diffing the "Subproject commit …" line
            // it synthesises, not lines of anyone's code. Counting it would put a fiction in the
            // statistics, so submodules contribute nothing.
            var countable = measured && !entry.IsSubmodule && stats.LinesAdded is not null;

            files.Add(new ChangedFile
            {
                Path = entry.Path,
                PreviousPath = entry.PreviousPath,
                Status = entry.Status,
                LinesAdded = countable ? stats.LinesAdded : null,
                LinesRemoved = countable ? stats.LinesRemoved : null,
                HunkCount = countable && hunks is not null ? hunks[index] : null,
                IsBinary = measured && !entry.IsSubmodule && stats.LinesAdded is null,
                IsSubmodule = entry.IsSubmodule,
                SubmoduleFromCommit = entry.SubmoduleFromCommit,
                SubmoduleToCommit = entry.SubmoduleToCommit
                    ?? await ResolveSubmoduleHeadAsync(root, entry, cancellationToken).ConfigureAwait(false),
                IsSymlink = entry.IsSymlink,
                Language = entry.IsSubmodule ? null : LanguageTable.Detect(entry.Path),
                Project = locator.Locate(entry.Path),
            });
        }

        if (query.IncludeUntracked)
        {
            await foreach (var untracked in ReadUntrackedAsync(root, locator, cancellationToken)
                               .ConfigureAwait(false))
            {
                files.Add(untracked);
            }
        }

        LoadedChangeset(logger, files.Count, root, query.IncludeUntracked);

        return new Changeset
        {
            RepositoryPath = root,
            IsClean = files.Count == 0,
            HasCommits = baseRevision.HasCommits,
            UntrackedIncluded = query.IncludeUntracked,
            Files = files,
            Statistics = ChangesetStatistics.From(files),
            HunkCountsAvailable = hunks is not null,
        };
    }

    /// <summary>
    /// Asks a submodule where its own <c>HEAD</c> is.
    /// <para>
    /// The working-tree side of a gitlink is all zeros in <c>--raw</c>, because the commit the
    /// submodule is currently on is not an object in the outer repository. Without this, the
    /// changeset could say what the submodule pointer <i>was</i> and never what it <i>is</i>,
    /// which is the more interesting half. One extra process per submodule, and repositories
    /// have a handful at most.
    /// </para>
    /// </summary>
    private async Task<string?> ResolveSubmoduleHeadAsync(
        string root,
        GitRawEntry entry,
        CancellationToken cancellationToken)
    {
        if (!entry.IsSubmodule || entry.DestinationAbsent)
        {
            return null;
        }

        var directory = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            // Declared in .gitmodules but never initialised. Nothing to ask.
            return null;
        }

        var head = await runner
            .RunAsync("rev-parse", ["--verify", "--quiet", "HEAD"], directory, cancellationToken)
            .ConfigureAwait(false);

        return head.Succeeded ? head.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// Pass one. The authority on which files changed, what happened to them and what kind of
    /// thing they are — symlink, submodule or ordinary file — read straight off the mode bits.
    /// </summary>
    private async Task<List<GitRawEntry>> ReadRawEntriesAsync(
        string root,
        string baseRevision,
        CancellationToken cancellationToken)
    {
        var entries = new List<GitRawEntry>();

        var outcome = await RunDiffAsync(
            root,
            ["--raw", "-z", "--no-abbrev", .. CommonDiffOptions, baseRevision, "--"],
            async (stream, token) =>
            {
                await foreach (var entry in GitOutputReaders.ReadRawAsync(stream, token).ConfigureAwait(false))
                {
                    entries.Add(entry);
                }
            },
            cancellationToken).ConfigureAwait(false);

        RequireSuccess(outcome, "diff --raw");
        return entries;
    }

    /// <summary>
    /// Pass two. Line counts, keyed by post-image path — the same path <c>--raw</c> reports, and
    /// safe to key on because both come out of <c>-z</c> output rather than a formatted header.
    /// </summary>
    private async Task<Dictionary<string, GitNumstatEntry>> ReadNumstatAsync(
        string root,
        string baseRevision,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, GitNumstatEntry>(StringComparer.Ordinal);

        var outcome = await RunDiffAsync(
            root,
            ["--numstat", "-z", .. CommonDiffOptions, baseRevision, "--"],
            async (stream, token) =>
            {
                await foreach (var entry in GitOutputReaders.ReadNumstatAsync(stream, token).ConfigureAwait(false))
                {
                    counts[entry.Path] = entry;
                }
            },
            cancellationToken).ConfigureAwait(false);

        RequireSuccess(outcome, "diff --numstat");
        return counts;
    }

    /// <summary>
    /// Pass three. Hunks per file, attributed by position because the patch stream's paths are
    /// the one thing in git's output this application refuses to parse.
    /// </summary>
    /// <returns>
    /// Null when the patch produced a different number of sections than <c>--raw</c> produced
    /// entries. Reporting no hunk counts is honest; reporting them against the wrong files is not.
    /// </returns>
    private async Task<IReadOnlyList<int>?> ReadHunkCountsAsync(
        string root,
        string baseRevision,
        int expectedSections,
        CancellationToken cancellationToken)
    {
        if (expectedSections == 0)
        {
            return [];
        }

        var scanner = new PatchHunkScanner();

        var outcome = await RunDiffAsync(
            root,
            ["-U0", .. CommonDiffOptions, baseRevision, "--"],
            async (stream, token) =>
            {
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    scanner.Feed(buffer.AsSpan(0, read));
                }

                scanner.Complete();
            },
            cancellationToken).ConfigureAwait(false);

        RequireSuccess(outcome, "diff -U0");

        if (scanner.Sections.Count == expectedSections)
        {
            return scanner.Sections;
        }

        HunkCountsUnattributable(logger, scanner.Sections.Count, expectedSections);
        return null;
    }

    /// <summary>
    /// Pass four. Untracked, not-ignored files, which <c>git diff</c> never shows and which an
    /// AI-generated change is full of. Omitting them is the most common way this layer gets
    /// quietly wrong, and it would violate §0.2.5 without anything failing.
    /// </summary>
    private async IAsyncEnumerable<ChangedFile> ReadUntrackedAsync(
        string root,
        ProjectLocator locator,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var paths = new List<string>();

        var outcome = await runner.RunStreamingAsync(
            "ls-files",
            // --exclude-standard is what keeps gitignored files out, in both toggle modes.
            ["--others", "--exclude-standard", "-z"],
            root,
            async (stream, token) =>
            {
                var reader = new NulFieldReader(stream);
                while (await reader.ReadFieldAsync(token).ConfigureAwait(false) is { } path)
                {
                    if (path.Length > 0)
                    {
                        paths.Add(path);
                    }
                }
            },
            cancellationToken,
            timeout: GitProcessRunner.ChangesetTimeout).ConfigureAwait(false);

        RequireSuccess(outcome, "ls-files --others");

        foreach (var path in paths)
        {
            yield return await DescribeUntrackedAsync(root, path, locator, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<ChangedFile> DescribeUntrackedAsync(
        string root,
        string path,
        ProjectLocator locator,
        CancellationToken cancellationToken)
    {
        // A nested repository comes back as a single entry with a trailing slash: git will not
        // look inside a repository it does not own. It is still a change, so it is still listed.
        if (path.EndsWith('/'))
        {
            var trimmed = path.TrimEnd('/');
            return new ChangedFile
            {
                Path = trimmed,
                Status = ChangeStatus.Added,
                IsBinary = false,
                IsUntracked = true,
                IsNestedRepository = true,
                Project = locator.Locate(trimmed),
            };
        }

        var absolute = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        var measured = await UntrackedFileFacts.MeasureAsync(absolute, cancellationToken).ConfigureAwait(false);

        return new ChangedFile
        {
            Path = path,
            Status = ChangeStatus.Added,
            LinesAdded = measured.Lines,
            LinesRemoved = measured.Lines is null ? null : 0,
            HunkCount = measured.Lines is null or 0 ? measured.Lines : 1,
            IsBinary = measured.IsBinary,
            IsSymlink = measured.IsSymlink,
            IsUntracked = true,
            Language = LanguageTable.Detect(path),
            Project = locator.Locate(path),
        };
    }

    /// <summary>Runs one whole-changeset diff pass.</summary>
    private Task<GitStreamOutcome> RunDiffAsync(
        string root,
        IReadOnlyList<string> arguments,
        Func<Stream, CancellationToken, Task> read,
        CancellationToken cancellationToken) =>
        runner.RunStreamingAsync(
            "diff",
            arguments,
            root,
            read,
            cancellationToken,
            timeout: GitProcessRunner.ChangesetTimeout);

    /// <summary>
    /// <c>HEAD</c> normally; the empty tree in a repository with no commits, so a first commit's
    /// worth of work still reviews as a changeset rather than failing.
    /// </summary>
    private async Task<(string Revision, bool HasCommits)> ResolveBaseRevisionAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var head = await runner
            .RunAsync("rev-parse", ["--verify", "--quiet", "HEAD"], root, cancellationToken)
            .ConfigureAwait(false);

        if (head.Succeeded)
        {
            return ("HEAD", true);
        }

        if (head.CouldNotRun)
        {
            throw Unavailable(head);
        }

        var format = await runner
            .RunAsync("rev-parse", ["--show-object-format"], root, cancellationToken)
            .ConfigureAwait(false);

        var sha256 = format.Succeeded &&
            format.StandardOutput.Trim().Equals("sha256", StringComparison.Ordinal);

        return (sha256 ? EmptyTreeSha256 : EmptyTreeSha1, false);
    }

    /// <summary>
    /// Confirms the path is a working tree before anything else runs, so a bad path fails with
    /// "that is not a repository" rather than with whatever git says four commands later.
    /// </summary>
    private async Task<string> RequireRepositoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var probe = await environment.ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (!probe.Available)
        {
            throw new GitClientException("git is not available on PATH.", GitClientFailure.GitUnavailable);
        }

        if (!Directory.Exists(repositoryPath))
        {
            throw new GitClientException(
                $"'{repositoryPath}' does not exist.",
                GitClientFailure.RepositoryUnreadable);
        }

        var toplevel = await runner
            .RunAsync("rev-parse", ["--show-toplevel"], repositoryPath, cancellationToken)
            .ConfigureAwait(false);

        if (toplevel.CouldNotRun)
        {
            throw Unavailable(toplevel);
        }

        if (!toplevel.Succeeded || string.IsNullOrWhiteSpace(toplevel.StandardOutput))
        {
            throw new GitClientException(
                $"'{repositoryPath}' is not inside a git working tree.",
                GitClientFailure.RepositoryUnreadable);
        }

        // git answers with forward slashes even on Windows; GetFullPath normalises them.
        return Path.GetFullPath(toplevel.StandardOutput.Trim());
    }

    private static void RequireSuccess(GitStreamOutcome outcome, string what)
    {
        if (outcome.Succeeded)
        {
            return;
        }

        if (outcome.CouldNotRun)
        {
            throw new GitClientException(
                outcome.TimedOutWaiting
                    ? $"git {what} did not finish in time and was stopped."
                    : $"git could not be run for {what}.",
                GitClientFailure.GitUnavailable);
        }

        throw new GitClientException(
            $"git {what} failed with exit code {outcome.ExitCode}: {outcome.StandardError.Trim()}",
            GitClientFailure.GitFailed);
    }

    private static GitClientException Unavailable(GitProcessResult result) =>
        new(
            result.TimedOutWaiting
                ? "git did not finish in time and was stopped."
                : "git could not be run.",
            GitClientFailure.GitUnavailable);

    [LoggerMessage(
        EventId = 2030,
        Level = LogLevel.Information,
        Message = "Changeset for {Repository}: {FileCount} file(s), untracked included: {IncludeUntracked}")]
    private static partial void LoadedChangeset(
        ILogger logger,
        int fileCount,
        string repository,
        bool includeUntracked);

    [LoggerMessage(
        EventId = 2031,
        Level = LogLevel.Warning,
        Message = "The patch stream had {Sections} section(s) for {Entries} changed file(s), so hunk counts were not attributed.")]
    private static partial void HunkCountsUnattributable(ILogger logger, int sections, int entries);
}
