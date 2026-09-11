using System.Security.Cryptography;
using DiffHacker.Core.Changes;
using DiffHacker.TestSupport;

namespace DiffHacker.Git.Tests;

/// <summary>
/// The content fingerprint a stored analysis keeps, so it can tell later whether the working tree
/// is still the change it describes. What matters is that the hash is of the bytes on disk, that it
/// exists only where there is a working-tree side to hash, and that asking for it is opt-in.
/// </summary>
public sealed class GitClientHashingTests
{
    private readonly GitClient _git = GitClientFactory.Create();

    [Fact]
    public async Task Hashing_is_off_unless_asked_for()
    {
        using var fixture = FixtureRepository.CreateWithCommit();
        fixture.WriteFile("untracked.txt", "new\n");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        changeset.File("untracked.txt").ContentSha256.ShouldBeNull(
            "the changeset panel has no use for a hash, and reading every file to make one is not free.");
    }

    [Fact]
    public async Task A_modified_an_untracked_and_a_renamed_file_are_hashed_from_their_bytes_on_disk()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("app.cs", "one\n");
        fixture.WriteFile("old-name.cs", string.Join('\n', Enumerable.Range(0, 40).Select(i => $"line {i}")) + "\n");
        fixture.Stage("app.cs", "old-name.cs");
        fixture.Commit("add");

        fixture.WriteFile("app.cs", "one\ntwo\n");
        fixture.WriteFile("brand-new.ts", "export const value = 1;\n");
        fixture.Rename("old-name.cs", "new-name.cs");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken, hashContent: true);

        changeset.File("app.cs").ContentSha256.ShouldBe(Sha256(fixture, "app.cs"));
        changeset.File("brand-new.ts").ContentSha256.ShouldBe(Sha256(fixture, "brand-new.ts"));
        changeset.File("new-name.cs").ContentSha256.ShouldBe(Sha256(fixture, "new-name.cs"));
    }

    [Fact]
    public async Task An_edit_that_keeps_the_line_counts_still_changes_the_hash_and_reverting_restores_it()
    {
        // The case the fingerprint exists for: +1 −1 before and after, so line counts alone would
        // call these the same file.
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("app.cs", "int tenant;\n");
        fixture.Stage("app.cs");
        fixture.Commit("add");

        fixture.WriteFile("app.cs", "int tenantId;\n");
        var first = (await _git.LoadAsync(fixture, TestContext.Current.CancellationToken, hashContent: true)).File("app.cs");

        fixture.WriteFile("app.cs", "int tenantKey;\n");
        var second = (await _git.LoadAsync(fixture, TestContext.Current.CancellationToken, hashContent: true)).File("app.cs");

        fixture.WriteFile("app.cs", "int tenantId;\n");
        var reverted = (await _git.LoadAsync(fixture, TestContext.Current.CancellationToken, hashContent: true)).File("app.cs");

        second.LinesAdded.ShouldBe(first.LinesAdded);
        second.LinesRemoved.ShouldBe(first.LinesRemoved);
        second.ContentSha256.ShouldNotBe(first.ContentSha256);
        reverted.ContentSha256.ShouldBe(first.ContentSha256);
    }

    [Fact]
    public async Task A_deleted_file_has_no_working_tree_side_and_so_no_hash()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("gone.cs", "soon\n");
        fixture.Stage("gone.cs");
        fixture.Commit("add");
        fixture.Delete("gone.cs");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken, hashContent: true);

        var gone = changeset.File("gone.cs");
        gone.Status.ShouldBe(ChangeStatus.Deleted);
        gone.ContentSha256.ShouldBeNull();
    }

    [Fact]
    public async Task A_symlink_is_hashed_by_where_it_points_and_never_followed()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("target.txt", "real content\n");
        fixture.Stage("target.txt");
        fixture.Commit("add target");

        if (!fixture.TryCreateSymlink("link.txt", "target.txt"))
        {
            Assert.Skip("This platform will not create symbolic links without elevation.");
        }

        fixture.Stage("link.txt");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken, hashContent: true);

        var link = changeset.File("link.txt").ContentSha256.ShouldNotBeNull();

        link.ShouldNotBe(
            Sha256(fixture, "target.txt"),
            "hashing the link's target content would follow it, possibly out of the repository.");

        // And editing the target does not change the link's identity: it still points at the same place.
        fixture.WriteFile("target.txt", "other content\n");

        (await _git.LoadAsync(fixture, TestContext.Current.CancellationToken, hashContent: true))
            .File("link.txt").ContentSha256.ShouldBe(link);
    }

    private static string Sha256(FixtureRepository fixture, string relativePath) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture.Absolute(relativePath))));
}
