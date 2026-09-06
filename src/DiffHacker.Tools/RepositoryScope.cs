using DiffHacker.Core.Changes;

namespace DiffHacker.Tools;

/// <summary>
/// The sandbox. Every path a tool is given passes through here, and nothing reaches the
/// filesystem that has not.
/// <para>
/// Requirement 4 is that the toolbox cannot escape the selected repository, and the verification
/// step spells out what that has to survive: <c>../../etc/passwd</c>, absolute paths, symlinks
/// pointing outside, and <c>.git/</c> internals. Each of those is refused by a different check
/// below, and they are layered on purpose — a string rule cannot see a symlink, and a filesystem
/// rule cannot see a path that was never meant to be resolved.
/// </para>
/// <para>
/// A refusal is a <see cref="PathRejection"/>, never an exception. The caller turns it into a
/// tool failure the model reads and corrects; an exception would end a run over a typo.
/// </para>
/// </summary>
public sealed class RepositoryScope
{
    private readonly IReadOnlySet<string> _visible;

    public RepositoryScope(string repositoryRoot, IReadOnlySet<string> visiblePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(visiblePaths);

        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        _visible = visiblePaths;
    }

    /// <summary>The worktree root, absolute and normalised.</summary>
    public string Root { get; }

    /// <summary>
    /// Resolves a caller-supplied path to a file the tools may read.
    /// </summary>
    public PathResolution ResolveFile(string? path)
    {
        var shape = ResolveShape(path);
        if (shape.Rejection is not PathRejection.None)
        {
            return shape;
        }

        // The visible set is git's answer to "what is in this repository", so membership is also
        // the .gitignore check and the does-it-exist check, at no extra cost.
        if (!_visible.Contains(shape.RelativePath))
        {
            return PathResolution.Rejected(PathRejection.NotVisible, shape.RelativePath);
        }

        return shape;
    }

    /// <summary>
    /// Resolves a path that names a directory, or the repository root when null or empty.
    /// <para>
    /// Directories are not in the visible set — git does not track them — so containment and the
    /// <c>.git/</c> rule are the whole of the check, and the caller decides whether a directory
    /// with no visible files underneath is worth reporting.
    /// </para>
    /// </summary>
    public PathResolution ResolveDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path is "." or "/" or "./")
        {
            return PathResolution.Accepted(string.Empty, Root);
        }

        return ResolveShape(path);
    }

    /// <summary>Whether a repository-relative path is one the tools may read.</summary>
    public bool IsVisible(string relativePath) => _visible.Contains(relativePath);

    /// <summary>
    /// The string and filesystem checks that apply to any path, file or directory.
    /// </summary>
    private PathResolution ResolveShape(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathResolution.Rejected(PathRejection.Empty, string.Empty);
        }

        // Absolute paths, rooted paths, and any . or .. segment. The classic traversal, refused
        // before anything touches a filesystem.
        if (!RepositoryPaths.IsRepositoryRelative(path, out var relative))
        {
            return PathResolution.Rejected(PathRejection.NotRepositoryRelative, path);
        }

        foreach (var segment in relative.Split('/'))
        {
            // Git's own directory, at any depth. Refused by name rather than by containment,
            // because it is inside the repository and containment alone would allow it.
            if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                return PathResolution.Rejected(PathRejection.GitInternals, relative);
            }

            // An NTFS alternate data stream: "notes.txt:hidden" is a different byte stream on the
            // same file, and Path.GetFullPath will happily keep it.
            if (segment.Contains(':', StringComparison.Ordinal))
            {
                return PathResolution.Rejected(PathRejection.NotRepositoryRelative, relative);
            }
        }

        var absolute = Path.GetFullPath(RepositoryPaths.ToAbsolute(Root, relative));

        // Belt and braces: the string rules should already have made this impossible, but the
        // containment check is cheap and it is the one that would catch a normalisation surprise
        // on some platform we have not tried.
        if (!RepositoryPaths.Contains(Root, absolute))
        {
            return PathResolution.Rejected(PathRejection.OutsideRepository, relative);
        }

        // Symlinks last, because it is the only check that costs syscalls. A link is legal inside
        // the repository — plenty of repositories have them — and illegal the moment its final
        // target lands outside, which no amount of string handling can see.
        if (EscapesThroughLink(absolute))
        {
            return PathResolution.Rejected(PathRejection.OutsideRepository, relative);
        }

        return PathResolution.Accepted(relative, absolute);
    }

    /// <summary>
    /// Whether the path, or any directory on the way to it, is a link whose final target lies
    /// outside the repository.
    /// <para>
    /// Every ancestor is checked, not just the leaf: a repository containing
    /// <c>vendor -&gt; /usr/lib</c> makes <c>vendor/anything</c> an escape even though
    /// <c>vendor/anything</c> is not itself a link.
    /// </para>
    /// </summary>
    private bool EscapesThroughLink(string absolute)
    {
        var current = absolute;

        while (!string.IsNullOrEmpty(current) && !RepositoryPaths.PathsEqual(current, Root))
        {
            try
            {
                var target = File.Exists(current)
                    ? File.ResolveLinkTarget(current, returnFinalTarget: true)
                    : Directory.ResolveLinkTarget(current, returnFinalTarget: true);

                if (target is not null && !RepositoryPaths.Contains(Root, Path.GetFullPath(target.FullName)))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A broken link, or one we may not read. Unreadable is not an escape; the read
                // that follows will report the file as absent on its own terms.
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || RepositoryPaths.PathsEqual(parent, current))
            {
                break;
            }

            current = parent;
        }

        return false;
    }
}

/// <summary>Why a path was refused.</summary>
public enum PathRejection
{
    None,

    /// <summary>No path was given.</summary>
    Empty,

    /// <summary>Absolute, rooted, containing <c>.</c> or <c>..</c>, or naming an alternate data stream.</summary>
    NotRepositoryRelative,

    /// <summary>Inside <c>.git/</c>.</summary>
    GitInternals,

    /// <summary>Resolved, through a link or otherwise, to somewhere outside the repository.</summary>
    OutsideRepository,

    /// <summary>Well-formed and inside the repository, but not a file git can see.</summary>
    NotVisible,
}

/// <summary>The outcome of putting one path through <see cref="RepositoryScope"/>.</summary>
public readonly record struct PathResolution
{
    public required PathRejection Rejection { get; init; }

    /// <summary>Normalised, forward-slashed, relative to the root. Empty for the root itself.</summary>
    public required string RelativePath { get; init; }

    /// <summary>The absolute path, only meaningful when <see cref="Accepted"/>.</summary>
    public required string AbsolutePath { get; init; }

    public bool IsAccepted => Rejection is PathRejection.None;

    public static PathResolution Accepted(string relativePath, string absolutePath) =>
        new()
        {
            Rejection = PathRejection.None,
            RelativePath = relativePath,
            AbsolutePath = absolutePath,
        };

    public static PathResolution Rejected(PathRejection rejection, string relativePath) =>
        new()
        {
            Rejection = rejection,
            RelativePath = relativePath,
            AbsolutePath = string.Empty,
        };

    /// <summary>
    /// What the model is told. Each reason says what to do instead, because a tool failure the
    /// model cannot act on is a wasted turn.
    /// </summary>
    public string Explain() => Rejection switch
    {
        PathRejection.Empty =>
            "No path was given. Pass a path relative to the repository root, such as src/app/main.ts.",
        PathRejection.NotRepositoryRelative =>
            $"'{RelativePath}' is not a repository-relative path. Paths must be relative to the "
            + "repository root, with no leading slash, no drive letter and no '..' segments.",
        PathRejection.GitInternals =>
            "Paths inside .git/ are not readable. Use list_changed_files, get_file_diff or "
            + "read_file with side='head' for anything you wanted from git's own storage.",
        PathRejection.OutsideRepository =>
            $"'{RelativePath}' resolves outside the repository and is not readable.",
        PathRejection.NotVisible =>
            $"'{RelativePath}' is not a file git can see. It may not exist, or it may be covered "
            + "by .gitignore — call get_path_info to find out which, or find_files to locate the "
            + "path you meant.",
        _ => $"'{RelativePath}' cannot be read.",
    };
}
