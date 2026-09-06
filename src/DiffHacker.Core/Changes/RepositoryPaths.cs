namespace DiffHacker.Core.Changes;

/// <summary>
/// The rules for talking about a path inside a repository, in one place.
/// <para>
/// Before Iteration 5 these lived twice, both times private: <c>GitClient</c> decided what counts
/// as a repository-relative path, and <c>RepositoryLocator</c> decided when two absolute paths are
/// the same path. The toolbox needs both, and a sandbox whose containment rule disagrees with the
/// git layer's path rule is a sandbox with a seam in it.
/// </para>
/// <para>
/// Nothing here touches the filesystem. Symlink resolution is a separate, more expensive question
/// and belongs to whoever is enforcing a boundary, not to string handling.
/// </para>
/// </summary>
public static class RepositoryPaths
{
    /// <summary>
    /// How to compare two paths for equality on this operating system.
    /// <para>
    /// Linux filesystems are case-sensitive; Windows and macOS are conventionally not. Getting
    /// this backwards on Windows means <c>C:\Repo\src</c> and <c>c:\repo\SRC</c> compare as
    /// different directories, which in a containment check reads as "outside the repository".
    /// </para>
    /// </summary>
    public static StringComparison PlatformComparison { get; } =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Git's spelling of a path: forward slashes, no repeated separators, no trailing separator.
    /// </summary>
    public static string Normalise(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var slashed = path.Replace('\\', '/');

        while (slashed.Contains("//", StringComparison.Ordinal))
        {
            slashed = slashed.Replace("//", "/", StringComparison.Ordinal);
        }

        return slashed.TrimEnd('/');
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a plain path relative to a repository root, and its
    /// normalised spelling if so.
    /// <para>
    /// Refuses absolute and rooted paths, and any <c>.</c> or <c>..</c> segment. This is the
    /// string half of the sandbox: necessary, and on its own nowhere near sufficient — a
    /// perfectly well-formed relative path can still lead outside through a symlink.
    /// </para>
    /// </summary>
    public static bool IsRepositoryRelative(string? path, out string normalised)
    {
        normalised = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidate = Normalise(path);

        if (candidate.Length == 0 || candidate.StartsWith('/') || Path.IsPathRooted(candidate))
        {
            return false;
        }

        foreach (var segment in candidate.Split('/'))
        {
            if (segment is "." or "..")
            {
                return false;
            }
        }

        normalised = candidate;
        return true;
    }

    /// <summary>
    /// Whether two absolute paths name the same location, ignoring a trailing separator and
    /// honouring the platform's case rule.
    /// </summary>
    public static bool PathsEqual(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return string.Equals(TrimSeparator(left), TrimSeparator(right), PlatformComparison);
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is <paramref name="root"/> or sits underneath it.
    /// <para>
    /// Compares whole segments, so <c>/repo-backup</c> is not inside <c>/repo</c> — the naive
    /// <c>StartsWith(root)</c> says it is.
    /// </para>
    /// </summary>
    public static bool Contains(string root, string candidate)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(candidate);

        var trimmedRoot = TrimSeparator(root);
        var trimmedCandidate = TrimSeparator(candidate);

        if (string.Equals(trimmedRoot, trimmedCandidate, PlatformComparison))
        {
            return true;
        }

        return trimmedCandidate.StartsWith(trimmedRoot, PlatformComparison)
            && trimmedCandidate.Length > trimmedRoot.Length
            && (trimmedCandidate[trimmedRoot.Length] == Path.DirectorySeparatorChar
                || trimmedCandidate[trimmedRoot.Length] == Path.AltDirectorySeparatorChar);
    }

    /// <summary>Joins a repository-relative path onto a root, in the platform's own separators.</summary>
    public static string ToAbsolute(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string TrimSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
