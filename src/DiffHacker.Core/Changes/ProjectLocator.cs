namespace DiffHacker.Core.Changes;

/// <summary>
/// Attributes a file to a project or module by finding the nearest manifest above it.
/// <para>
/// Ecosystem-agnostic by construction: the manifest set is a list of filenames and glob-free
/// suffixes, and a match is decided by name alone. Nothing is opened, so nothing here can drift
/// into parsing a build system (§0.2.3, and requirement 4's "this is metadata only").
/// </para>
/// <para>
/// Nearest wins. A file under <c>src/Web/</c> with a <c>package.json</c> two levels up belongs
/// to that project, not to the repository root, even when the root also has a manifest.
/// </para>
/// <para>
/// One instance per changeset run: it caches per directory, so a 1500-file changeset costs one
/// listing per distinct directory rather than one per file. Not thread-safe, and does not need
/// to be.
/// </para>
/// </summary>
public sealed class ProjectLocator
{
    /// <summary>
    /// Exact manifest filenames, ordinal ignore-case. One per ecosystem the plan names, plus the
    /// ones that show up constantly in mixed repositories.
    /// </summary>
    private static readonly HashSet<string> ManifestNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "package.json",
            "deno.json",
            "deno.jsonc",
            "pyproject.toml",
            "setup.py",
            "go.mod",
            "pom.xml",
            "build.gradle",
            "build.gradle.kts",
            "settings.gradle",
            "settings.gradle.kts",
            "Cargo.toml",
            "composer.json",
            "Gemfile",
            "CMakeLists.txt",
            "pubspec.yaml",
            "mix.exs",
            "build.sbt",
            "Package.swift",
            "stack.yaml",
            "cabal.project",
            "Makefile",
        };

    /// <summary>
    /// Manifests identified by extension rather than by name — the .NET project files, whose
    /// stem is the project name.
    /// </summary>
    private static readonly string[] ManifestExtensions =
        [".csproj", ".fsproj", ".vbproj", ".gemspec", ".podspec", ".cabal"];

    private readonly string _repositoryRoot;
    private readonly string _repositoryName;
    private readonly Func<string, IReadOnlyList<string>> _listDirectory;

    /// <summary>Manifest found for a repository-relative directory, or null when there is none.</summary>
    private readonly Dictionary<string, ProjectReference?> _cache = new(StringComparer.Ordinal);

    /// <param name="repositoryRoot">Absolute path of the worktree root.</param>
    /// <param name="listDirectory">
    /// Returns the file names directly inside an absolute directory path. Injectable so the
    /// resolution rules can be tested without building a directory tree on disk; defaults to the
    /// real filesystem.
    /// </param>
    public ProjectLocator(string repositoryRoot, Func<string, IReadOnlyList<string>>? listDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = repositoryRoot;
        _repositoryName = LeafName(repositoryRoot);
        _listDirectory = listDirectory ?? (directory => ListDirectoryOnDisk(directory));
    }

    /// <summary>Attributes one repository-relative file path to a project.</summary>
    public ProjectReference Locate(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var directory = ParentDirectory(relativePath);

        // Walk up from the file's own directory to the root, nearest first.
        var candidate = directory;
        while (true)
        {
            var found = ManifestIn(candidate);
            if (found is not null)
            {
                return found.Value;
            }

            if (candidate.Length == 0)
            {
                break;
            }

            candidate = ParentDirectory(candidate);
        }

        return Fallback(directory);
    }

    /// <summary>
    /// No manifest anywhere above the file. Attribute it to its top-level directory, which in
    /// practice is what people mean by "which part of the repository is this" — and to the
    /// repository itself for a file sitting at the root.
    /// </summary>
    private ProjectReference Fallback(string directory)
    {
        if (directory.Length == 0)
        {
            return new ProjectReference(_repositoryName, string.Empty, null);
        }

        var firstSlash = directory.IndexOf('/');
        var top = firstSlash < 0 ? directory : directory[..firstSlash];

        return new ProjectReference(top, top, null);
    }

    private ProjectReference? ManifestIn(string relativeDirectory)
    {
        if (_cache.TryGetValue(relativeDirectory, out var cached))
        {
            return cached;
        }

        var absolute = relativeDirectory.Length == 0
            ? _repositoryRoot
            : Path.Combine(_repositoryRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        ProjectReference? result = null;

        foreach (var fileName in _listDirectory(absolute))
        {
            if (!IsManifest(fileName))
            {
                continue;
            }

            var manifestPath = relativeDirectory.Length == 0 ? fileName : relativeDirectory + "/" + fileName;

            result = new ProjectReference(
                ProjectNameFor(fileName, relativeDirectory),
                relativeDirectory,
                manifestPath);
            break;
        }

        _cache[relativeDirectory] = result;
        return result;
    }

    /// <summary>
    /// A .NET project file names its project; every other manifest is a fixed filename, so the
    /// directory it sits in is the only meaningful name available without opening it.
    /// </summary>
    private string ProjectNameFor(string manifestFileName, string relativeDirectory)
    {
        foreach (var extension in ManifestExtensions)
        {
            if (manifestFileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return manifestFileName[..^extension.Length];
            }
        }

        if (relativeDirectory.Length == 0)
        {
            return _repositoryName;
        }

        var lastSlash = relativeDirectory.LastIndexOf('/');
        return lastSlash < 0 ? relativeDirectory : relativeDirectory[(lastSlash + 1)..];
    }

    private static bool IsManifest(string fileName)
    {
        if (ManifestNames.Contains(fileName))
        {
            return true;
        }

        foreach (var extension in ManifestExtensions)
        {
            if (fileName.Length > extension.Length &&
                fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Parent of a git path, or an empty string for something already at the root.</summary>
    private static string ParentDirectory(string relativePath)
    {
        var lastSlash = relativePath.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : relativePath[..lastSlash];
    }

    private static string LeafName(string root)
    {
        var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? root : leaf;
    }

    private static List<string> ListDirectoryOnDisk(string absoluteDirectory)
    {
        try
        {
            var names = new List<string>();
            foreach (var path in Directory.EnumerateFiles(absoluteDirectory))
            {
                names.Add(Path.GetFileName(path));
            }

            return names;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A directory we cannot list simply has no manifest as far as attribution goes.
            // Failing the whole changeset over one unreadable folder would be absurd.
            return [];
        }
    }
}
