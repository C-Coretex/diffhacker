using System.Text.RegularExpressions;

namespace DiffHacker.Architecture.Tests;

/// <summary>
/// Iteration 6's verification step 6, as an assertion: "confirm no other code path in the entire
/// application writes into the repository — audit it, do not assume it".
/// <para>
/// §0.2.12 makes DiffHacker read-only with exactly one exception, the documentation export, and an
/// audit is worth something once and then rots. This enumerates every source file in the product
/// and asserts that filesystem-write APIs appear only where they are supposed to.
/// </para>
/// <para>
/// The allowlist below is short on purpose, and every entry writes into the application's own data
/// directory rather than into a repository — except the first, which is the exception §0.2.12
/// names. Adding to it is a change to the product's contract, not a detail: a reviewer who sees
/// this list grow should ask why.
/// </para>
/// </summary>
public sealed partial class RepositoryWriteTests
{
    /// <summary>
    /// Files permitted to hold a write API, and what each of them writes to.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        // The one exception in §0.2.12. Gated by a preview the user confirms, and by a token the
        // host recomputes from the bytes it is about to write.
        ["src/DiffHacker.Host/Knowledge/RepositoryDocumentationWriter.cs"] = "the documentation export",

        // The application's own data directory, never a repository.
        ["src/DiffHacker.Storage/Secrets/FileSecretStore.cs"] = "the encrypted secret file",
        ["src/DiffHacker.Storage/Secrets/SecretFilePermissions.cs"] = "the secret file's permissions",
        ["src/DiffHacker.Storage/Secrets/DpapiMasterKeyProtector.cs"] = "the master key file",
        ["src/DiffHacker.Storage/Secrets/MachineDerivedMasterKeyProtector.cs"] = "the salt file",
        ["src/DiffHacker.Host/AppPaths.cs"] = "the data and log directories",
        ["src/DiffHacker.Host/Logging/LoggingSetup.cs"] = "log.txt",

        // Iteration 10. An external diff tool takes two file paths and the committed side of a change
        // is a git object, so something has to materialise it. This writes it under
        // AppPaths.DiffCacheDirectory — inside the application's own data directory — and checks the
        // containment itself before writing, which is why this entry is not a hole in the rule above.
        ["src/DiffHacker.Host/Editor/HeadBlobExtractor.cs"] = "the extracted HEAD side, in the diff cache",

        // "Delete all local data": wipes the database, secrets, diff cache and log files under
        // AppPaths.DataDirectory, then leaves a marker for the one file it could not remove — the
        // active log — to be swept up at the next launch, before logging reopens it.
        ["src/DiffHacker.Host/Rpc/DataRpcTarget.cs"] = "the database, secret and diff-cache files, and old logs",
        ["src/DiffHacker.Host/PendingDataWipe.cs"] = "the log directory, finishing a wipe from the previous run",
    };

    [Fact]
    public void The_product_has_files_to_check()
    {
        // Without this the rule below would pass by matching nothing at all.
        ProductFiles().ShouldNotBeEmpty();
    }

    [Fact]
    public void Only_the_documentation_export_can_write_into_a_repository()
    {
        var offenders = ProductFiles()
            .Select(static path => (
                Path: RepositoryLayout.RelativePath(path),
                Code: RepositoryLayout.CodeWithoutComments(path)))
            .Where(file => WriteApi().IsMatch(file.Code))
            .Select(static file => file.Path)
            .Where(static path => !Allowed.ContainsKey(path))
            .ToArray();

        offenders.ShouldBeEmpty(
            "DiffHacker is read-only apart from the opt-in documentation export (§0.2.12). "
            + "A new write path is a change to the product's contract: if it belongs, add it to "
            + "RepositoryWriteTests.Allowed with a note saying what it writes to, and make sure it "
            + "is not a repository.");
    }

    [Fact]
    public void Every_allowed_file_still_exists()
    {
        // An allowlist that outlives the file it names is an allowlist that has stopped meaning
        // anything, and the next write path added under that name would be permitted silently.
        var present = ProductFiles().Select(RepositoryLayout.RelativePath).ToHashSet(StringComparer.Ordinal);

        Allowed.Keys.Where(path => !present.Contains(path)).ShouldBeEmpty();
    }

    /// <summary>
    /// Every shipped source file. Tests are excluded: they write fixture repositories on purpose,
    /// which is the whole of how the read-only rules above are exercised.
    /// </summary>
    private static IEnumerable<string> ProductFiles() =>
        RepositoryLayout.SourceFiles()
            .Where(static path => RepositoryLayout.RelativePath(path)
                .StartsWith("src/", StringComparison.Ordinal));

    /// <summary>
    /// Writing, specifically.
    /// <para>
    /// A bare <c>new FileStream</c> is deliberately not here, where <c>ToolboxSandboxTests</c> does
    /// forbid it: the toolbox has no business opening a stream at all, but the git layer and the
    /// asset resolver both read one, and a rule that flagged a read would have to be silenced with
    /// allowlist entries that then hid a real write. So the pattern names the writing modes and
    /// accesses instead — a stream opened <c>FileMode.Open, FileAccess.Read</c> is not a write and
    /// is not reported as one.
    /// </para>
    /// </summary>
    [GeneratedRegex(
        @"\bFile\s*\.\s*(Write|Create|Delete|Move|Copy|Append|Replace|Encrypt|Decrypt|SetAttributes)"
        + @"|\bDirectory\s*\.\s*(Create|Delete|Move)"
        + @"|\bnew\s+StreamWriter\b"
        + @"|\bFileMode\s*\.\s*(Create|CreateNew|Append|Truncate|OpenOrCreate)"
        + @"|\bFileAccess\s*\.\s*(Write|ReadWrite)")]
    private static partial Regex WriteApi();
}
