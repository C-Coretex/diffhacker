using System.Reflection;

namespace DiffHacker.Architecture.Tests;

/// <summary>
/// Locates the repository on disk, so the source-level rules can read the files they police.
/// </summary>
internal static partial class RepositoryLayout
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

    /// <summary>
    /// A file's code with its comments removed.
    /// <para>
    /// Source-text rules that police a namespace need this. The comment explaining why a layer
    /// must not reference something necessarily names the thing it must not reference, and a
    /// rule that cannot tell the two apart either fails on its own documentation or has to go
    /// undocumented.
    /// </para>
    /// <para>
    /// Deliberately crude: it does not understand string literals, so a <c>"//"</c> inside one
    /// truncates that line. For "does this file mention a namespace" that is harmless, and the
    /// alternative is a parser.
    /// </para>
    /// </summary>
    public static string CodeWithoutComments(string path)
    {
        var text = File.ReadAllText(path);
        text = BlockComment().Replace(text, " ");
        return LineComment().Replace(text, string.Empty);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"/\*.*?\*/", System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex BlockComment();

    [System.Text.RegularExpressions.GeneratedRegex(@"//.*$", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex LineComment();

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
