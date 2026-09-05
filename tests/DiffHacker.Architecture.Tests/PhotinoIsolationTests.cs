namespace DiffHacker.Architecture.Tests;

/// <summary>
/// CLAUDE.md §0.3: "Photino types must not appear anywhere outside the <c>IAppShell</c>
/// implementation."
/// <para>
/// An assembly-reference check cannot express this — <c>DiffHacker.Host</c> legitimately
/// references Photino — so the rule is enforced against the source text instead. If the
/// shell implementation is ever renamed or split, update <see cref="PermittedFiles"/>
/// deliberately rather than by reflex.
/// </para>
/// </summary>
public sealed class PhotinoIsolationTests
{
    private static readonly string[] PermittedFiles =
    [
        "src/DiffHacker.Host/Shell/PhotinoAppShell.cs",
    ];

    [Fact]
    public void Only_the_shell_implementation_mentions_Photino()
    {
        var offenders = RepositoryLayout.SourceFiles()
            .Where(static path => File.ReadAllText(path).Contains("Photino.NET", StringComparison.Ordinal)
                                  || File.ReadAllText(path).Contains("using Photino", StringComparison.Ordinal))
            .Select(RepositoryLayout.RelativePath)
            .Where(relative => !PermittedFiles.Contains(relative, StringComparer.Ordinal))
            .ToArray();

        offenders.ShouldBeEmpty(
            "Photino must stay behind IAppShell. Offending files: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_permitted_file_actually_exists()
    {
        // Guards against the rule silently passing because the file was renamed.
        foreach (var relative in PermittedFiles)
        {
            File.Exists(Path.Combine(RepositoryLayout.Root, relative))
                .ShouldBeTrue($"{relative} is on the Photino allow-list but does not exist.");
        }
    }
}
