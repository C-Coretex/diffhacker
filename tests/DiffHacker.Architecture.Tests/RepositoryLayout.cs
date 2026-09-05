using System.Reflection;

namespace DiffHacker.Architecture.Tests;

/// <summary>
/// Locates the repository on disk, so the source-level rules can read the files they police.
/// </summary>
internal static class RepositoryLayout
{
    public static string Root { get; } = ResolveRoot();

    public static string SourceDirectory => Path.Combine(Root, "src");

    /// <summary>Every C# file under <c>/src</c>, excluding build output and generated code.</summary>
    public static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static path => !path.EndsWith(".g.cs", StringComparison.Ordinal));

    public static string RelativePath(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string ResolveRoot()
    {
        var stamped = typeof(RepositoryLayout).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "DiffHacker.RepoRoot")?.Value;

        if (!string.IsNullOrWhiteSpace(stamped) && Directory.Exists(Path.Combine(stamped, "src")))
        {
            return Path.GetFullPath(stamped);
        }

        // Fall back to walking up from the test binary, so the suite still runs if the
        // stamped path is stale (a copied output directory, for instance).
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "DiffHacker.Host")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
