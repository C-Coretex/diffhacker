using DiffHacker.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Git;

/// <summary>
/// Resolves a user-chosen path to a working tree.
/// <para>
/// The rules, settled during Iteration 2 planning: a directory inside a repository resolves
/// upwards to the worktree root and the interface says so; a bare repository is rejected,
/// because it has no working tree and working-tree-vs-HEAD is the only thing this application
/// reviews (§0.2.11); linked worktrees, submodule directories and repositories with no commits
/// are all accepted.
/// </para>
/// <para>
/// A submodule directory is a working tree in its own right, so it is accepted as its own
/// repository. The user pointed at it deliberately.
/// </para>
/// </summary>
public sealed partial class RepositoryLocator(
    GitProcessRunner runner,
    IGitEnvironment environment,
    ILogger<RepositoryLocator> logger)
    : IRepositoryLocator
{
    public async ValueTask<RepositoryResolution> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var probe = await environment.ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (!probe.Available)
        {
            return RepositoryResolution.Rejected(RepositoryRejection.GitUnavailable);
        }

        var directory = DescribeDirectory(path);
        if (directory is not RepositoryRejection.None)
        {
            return RepositoryResolution.Rejected(directory);
        }

        var full = Path.GetFullPath(path);

        var bare = await runner.RunAsync("rev-parse", ["--is-bare-repository"], full, cancellationToken)
            .ConfigureAwait(false);

        if (bare.CouldNotRun)
        {
            return RepositoryResolution.Rejected(RepositoryRejection.GitUnavailable);
        }

        if (!bare.Succeeded)
        {
            RejectedPath(logger, full, bare.StandardError.Trim());
            return RepositoryResolution.Rejected(RepositoryRejection.NotARepository);
        }

        if (bare.StandardOutput.Trim().Equals("true", StringComparison.Ordinal))
        {
            return RepositoryResolution.Rejected(RepositoryRejection.BareRepository);
        }

        var toplevel = await runner.RunAsync("rev-parse", ["--show-toplevel"], full, cancellationToken)
            .ConfigureAwait(false);

        if (!toplevel.Succeeded || string.IsNullOrWhiteSpace(toplevel.StandardOutput))
        {
            return RepositoryResolution.Rejected(RepositoryRejection.NotARepository);
        }

        // git reports forward slashes even on Windows; GetFullPath normalises them.
        var root = Path.GetFullPath(toplevel.StandardOutput.Trim());

        var descriptor = new RepositoryDescriptor
        {
            Path = root,
            Name = LeafName(root),
            HasCommits = await HasCommitsAsync(root, cancellationToken).ConfigureAwait(false),
            IsLinkedWorktree = await IsLinkedWorktreeAsync(root, cancellationToken).ConfigureAwait(false),
        };

        return RepositoryResolution.Accepted(descriptor, !PathsEqual(root, full));
    }

    public ValueTask<bool> IsStillAvailableAsync(string path, CancellationToken cancellationToken)
    {
        // Deliberately no git process here: this runs once per row of the recent list, and
        // spawning a process per row to render a list would be absurd. "The directory is there
        // and still carries a .git" is the honest claim, and opening it validates properly.
        _ = cancellationToken;

        try
        {
            if (!Directory.Exists(path))
            {
                return ValueTask.FromResult(false);
            }

            var git = Path.Combine(path, ".git");
            return ValueTask.FromResult(Directory.Exists(git) || File.Exists(git));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult(false);
        }
    }

    private async ValueTask<bool> HasCommitsAsync(string root, CancellationToken cancellationToken)
    {
        var head = await runner
            .RunAsync("rev-parse", ["--verify", "--quiet", "HEAD"], root, cancellationToken)
            .ConfigureAwait(false);

        return head.Succeeded;
    }

    private async ValueTask<bool> IsLinkedWorktreeAsync(string root, CancellationToken cancellationToken)
    {
        // In a linked worktree --git-dir points at <common>/worktrees/<name> while
        // --git-common-dir points at <common>. In the main worktree they are the same.
        var dirs = await runner
            .RunAsync("rev-parse", ["--git-dir", "--git-common-dir"], root, cancellationToken)
            .ConfigureAwait(false);

        if (!dirs.Succeeded)
        {
            return false;
        }

        var lines = dirs.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            return false;
        }

        return !PathsEqual(Resolve(root, lines[0]), Resolve(root, lines[1]));
    }

    private static string Resolve(string root, string candidate) =>
        Path.GetFullPath(Path.IsPathRooted(candidate) ? candidate : Path.Combine(root, candidate));

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>Leaf directory name, coping with a path that is a drive or filesystem root.</summary>
    private static string LeafName(string root)
    {
        var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? root : leaf;
    }

    private static RepositoryRejection DescribeDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                // Touch the directory so a permissions problem surfaces as AccessDenied rather
                // than being reported as "not a repository" three calls later.
                _ = new DirectoryInfo(path).EnumerateFileSystemInfos().Any();
                return RepositoryRejection.None;
            }

            return RepositoryRejection.PathNotFound;
        }
        catch (UnauthorizedAccessException)
        {
            return RepositoryRejection.AccessDenied;
        }
        catch (IOException)
        {
            return RepositoryRejection.PathNotFound;
        }
    }

    [LoggerMessage(EventId = 2020, Level = LogLevel.Debug, Message = "{Path} was rejected by git rev-parse: {Detail}")]
    private static partial void RejectedPath(ILogger logger, string path, string detail);
}
