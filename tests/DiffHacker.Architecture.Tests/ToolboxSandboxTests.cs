using System.Text.RegularExpressions;

namespace DiffHacker.Architecture.Tests;

/// <summary>
/// Iteration 5's verification step 4, as an assertion: "not 'it is not called', but 'it does not
/// exist'".
/// <para>
/// The toolbox is handed to a language model. Requirement 4 says it cannot write, cannot run a
/// command and cannot reach the network — and a promise of that shape is only worth anything if
/// something checks the capability is absent rather than merely unused. Reviewing for it works
/// until the review someone skips.
/// </para>
/// <para>
/// Scoped to <c>src/DiffHacker.Tools</c>. The toolbox does run git, but only through
/// <c>IGitClient</c>, where the read-only subcommand allowlist applies — which is exactly the
/// point of the toolbox owning no process API of its own.
/// </para>
/// </summary>
public sealed partial class ToolboxSandboxTests
{
    private static IEnumerable<string> ToolboxFiles() =>
        RepositoryLayout.SourceFiles()
            .Where(static path => RepositoryLayout.RelativePath(path)
                .StartsWith("src/DiffHacker.Tools/", StringComparison.Ordinal));

    [Fact]
    public void The_toolbox_has_files_to_check()
    {
        // Without this the rules below would pass by matching nothing at all.
        ToolboxFiles().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_toolbox_cannot_start_a_process()
    {
        AssertAbsent(
            ProcessApi(),
            "The toolbox must contain no way to execute a command (Iteration 5, requirement 4). "
            + "Git runs through IGitClient, where the read-only subcommand allowlist applies.");
    }

    [Fact]
    public void The_toolbox_cannot_write_to_disk()
    {
        AssertAbsent(
            WriteApi(),
            "The toolbox must contain no write path at all (§0.2.12, Iteration 5 requirement 4). "
            + "It reads a repository it does not own.");
    }

    [Fact]
    public void The_toolbox_cannot_reach_the_network()
    {
        AssertAbsent(
            NetworkApi(),
            "The toolbox must contain no network access (Iteration 5, requirement 4).");
    }

    private static void AssertAbsent(Regex forbidden, string because)
    {
        var offenders = ToolboxFiles()
            // Comments are stripped first: this file's own prose, and the doc comments in the
            // toolbox explaining what it may not do, would otherwise fail the rules they explain.
            .Select(static path => (Path: RepositoryLayout.RelativePath(path),
                Code: RepositoryLayout.CodeWithoutComments(path)))
            .Where(file => forbidden.IsMatch(file.Code))
            .Select(static file => file.Path)
            .ToArray();

        offenders.ShouldBeEmpty(because);
    }

    [GeneratedRegex(@"\bProcess\s*\.\s*Start|\bProcessStartInfo\b|\bSystem\.Diagnostics\.Process\b")]
    private static partial Regex ProcessApi();

    [GeneratedRegex(
        @"\bFile\s*\.\s*(Write|Create|Delete|Move|Copy|Append|Replace|Encrypt|Decrypt|SetAttributes)"
        + @"|\bDirectory\s*\.\s*(Create|Delete|Move)"
        + @"|\bnew\s+StreamWriter\b"
        + @"|\bnew\s+FileStream\b"
        + @"|\bFileMode\s*\.\s*(Create|CreateNew|Append|Truncate|OpenOrCreate)")]
    private static partial Regex WriteApi();

    [GeneratedRegex(
        @"\bHttpClient\b|\bWebRequest\b|\bTcpClient\b|\bSocket\b|\bWebSocket\b|\bDns\s*\.")]
    private static partial Regex NetworkApi();
}
