using System.Text.RegularExpressions;

namespace DiffHacker.Architecture.Tests;

/// <summary>
/// CLAUDE.md lists "a local HTTP server or localhost port for serving UI assets" as
/// permanently out of scope. That constraint is what makes the WebView's CSP meaningful, so
/// it is worth a test rather than a comment.
/// </summary>
public sealed partial class NoLocalServerTests
{
    [Fact]
    public void No_source_file_reaches_for_a_localhost_url()
    {
        var offenders = RepositoryLayout.SourceFiles()
            .Where(static path => LocalhostUrl().IsMatch(File.ReadAllText(path)))
            .Select(RepositoryLayout.RelativePath)
            .ToArray();

        offenders.ShouldBeEmpty(
            "The renderer is served in-process through a custom scheme handler. Offending files: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void No_source_file_references_a_web_host_package()
    {
        var offenders = RepositoryLayout.SourceFiles()
            .Where(static path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("Microsoft.AspNetCore", StringComparison.Ordinal)
                       || text.Contains("HttpListener", StringComparison.Ordinal);
            })
            .Select(RepositoryLayout.RelativePath)
            .ToArray();

        offenders.ShouldBeEmpty("Offending files: " + string.Join(", ", offenders));
    }

    [GeneratedRegex(@"https?://(localhost|127\.0\.0\.1|\[::1\])", RegexOptions.IgnoreCase)]
    private static partial Regex LocalhostUrl();
}
