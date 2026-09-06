using System.Text;
using DiffHacker.Core.Changes;

namespace DiffHacker.Git.Tests;

/// <summary>
/// The on-demand half of the Git layer: one file's content, one file's diff.
/// <para>
/// The shape these return is a decision Iteration 10's diff viewer inherits, so the awkward
/// cases are tested rather than assumed: a deleted file has no working-tree side, an added file
/// has no HEAD side, a binary has no text, an oversized file has a size and nothing else.
/// </para>
/// </summary>
public sealed class GitClientContentTests
{
    private readonly GitClient _git = GitClientFactory.Create();

    [Fact]
    public async Task An_added_file_has_no_head_side_and_a_deleted_file_has_no_working_tree_side()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("doomed.cs", "class Doomed;\n");
        fixture.Stage("doomed.cs");
        fixture.Commit("add doomed");
        fixture.Delete("doomed.cs");

        fixture.WriteFile("fresh.cs", "class Fresh;\n");

        var deletedAfter = await _git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "doomed.cs", FileSide.WorkingTree),
            TestContext.Current.CancellationToken);

        var addedBefore = await _git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "fresh.cs", FileSide.Head),
            TestContext.Current.CancellationToken);

        // One rule for both, so the viewer has one branch rather than two null checks.
        deletedAfter.Kind.ShouldBe(FileContentKind.Absent, "A deleted file has no after side.");
        deletedAfter.Text.ShouldBeNull();
        addedBefore.Kind.ShouldBe(FileContentKind.Absent, "An added file has no before side.");
        addedBefore.Text.ShouldBeNull();

        var deletedBefore = await _git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "doomed.cs", FileSide.Head),
            TestContext.Current.CancellationToken);

        deletedBefore.Kind.ShouldBe(FileContentKind.Text);
        deletedBefore.Text.ShouldBe("class Doomed;\n");
    }

    [Fact]
    public async Task An_empty_file_is_text_rather_than_absent()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("empty.txt", string.Empty);

        var content = await _git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "empty.txt", FileSide.WorkingTree),
            TestContext.Current.CancellationToken);

        // The reason absence is a kind rather than an empty string: these two are different
        // files and the interface has to be able to tell the reviewer which one they are looking at.
        content.Kind.ShouldBe(FileContentKind.Text);
        content.Text.ShouldBe(string.Empty);
        content.SizeBytes.ShouldBe(0);
    }

    [Fact]
    public async Task A_binary_file_reports_its_size_and_no_text()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteBinaryFile("blob.bin", 4096);

        var content = await _git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "blob.bin", FileSide.WorkingTree),
            TestContext.Current.CancellationToken);

        content.Kind.ShouldBe(FileContentKind.Binary);
        content.Text.ShouldBeNull("A wall of bytes is not a diff, and pretending otherwise helps nobody.");
        content.SizeBytes.ShouldBe(4096);
    }

    [Fact]
    public async Task A_file_that_is_not_valid_utf8_is_decoded_and_says_so()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        // Latin-1 "café" — 0xE9 is a valid byte and an invalid UTF-8 sequence.
        fixture.WriteBytes("legacy.txt", [.. "caf"u8.ToArray(), 0xE9, .. "\n"u8.ToArray()]);

        var content = await _git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "legacy.txt", FileSide.WorkingTree),
            TestContext.Current.CancellationToken);

        content.Kind.ShouldBe(FileContentKind.Text);
        content.Text.ShouldBe("café\n");
        content.Encoding.ShouldBe(TextDecoding.Latin1);
        content.UsedFallbackEncoding.ShouldBeTrue(
            "Iteration 5 must be able to say the file was not UTF-8 rather than showing replacement characters.");
    }

    [Fact]
    public async Task A_utf8_file_with_a_byte_order_mark_keeps_its_content_and_names_its_encoding()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteBytes("bom.txt", [0xEF, 0xBB, 0xBF, .. "hello\n"u8.ToArray()]);

        var content = await _git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "bom.txt", FileSide.WorkingTree),
            TestContext.Current.CancellationToken);

        content.Text.ShouldBe("hello\n", "The mark is an encoding declaration, not content.");
        content.Encoding.ShouldBe(TextDecoding.Utf8Bom);
        content.UsedFallbackEncoding.ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_past_the_size_cap_reports_its_true_size_and_no_content()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        var size = ContentLimits.MaxBytes + (256 * 1024);
        fixture.WriteLargeTextFile("huge.txt", size);

        var content = await _git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "huge.txt", FileSide.WorkingTree),
            TestContext.Current.CancellationToken);

        content.Kind.ShouldBe(FileContentKind.TooLarge);
        content.Text.ShouldBeNull();
        content.SizeBytes.ShouldBeGreaterThan(ContentLimits.MaxBytes);
    }

    [Fact]
    public async Task A_tracked_file_diff_shows_the_change()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("app.cs", "one\ntwo\n");
        fixture.Stage("app.cs");
        fixture.Commit("baseline");
        fixture.WriteFile("app.cs", "one\ntwo modified\n");

        var diff = await _git.GetFileDiffAsync(
            new FileDiffQuery(fixture.Root, "app.cs"),
            TestContext.Current.CancellationToken);

        diff.Kind.ShouldBe(FileContentKind.Text);

        var patch = diff.UnifiedDiff.ShouldNotBeNull();
        patch.ShouldContain("-two");
        patch.ShouldContain("+two modified");
    }

    [Fact]
    public async Task A_renamed_file_diff_shows_the_move_rather_than_an_unrelated_add()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("before.cs", string.Join('\n', Enumerable.Range(0, 30).Select(i => $"line {i}")) + "\n");
        fixture.Stage("before.cs");
        fixture.Commit("baseline");
        fixture.Rename("before.cs", "after.cs");

        var diff = await _git.GetFileDiffAsync(
            new FileDiffQuery(fixture.Root, "after.cs", PreviousPath: "before.cs"),
            TestContext.Current.CancellationToken);

        diff.Kind.ShouldBe(FileContentKind.Text);
        diff.PreviousPath.ShouldBe("before.cs");

        var patch = diff.UnifiedDiff.ShouldNotBeNull();
        patch.ShouldContain("rename from before.cs");
        patch.ShouldContain("rename to after.cs");
    }

    [Fact]
    public async Task An_untracked_file_diff_carries_its_whole_content_as_the_added_side()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("new.ts", "const a = 1;\nconst b = 2;\n");

        var diff = await _git.GetFileDiffAsync(
            new FileDiffQuery(fixture.Root, "new.ts", Untracked: true),
            TestContext.Current.CancellationToken);

        // Requirement 2. Git will not diff a file it does not know about, so this side is built
        // from the file itself — and it has to look like a patch, because that is what everything
        // downstream expects to read.
        diff.Kind.ShouldBe(FileContentKind.Text);

        var patch = diff.UnifiedDiff.ShouldNotBeNull();
        patch.ShouldContain("diff --git a/new.ts b/new.ts");
        patch.ShouldContain("--- /dev/null");
        patch.ShouldContain("@@ -0,0 +1,2 @@");
        patch.ShouldContain("+const a = 1;");
        patch.ShouldContain("+const b = 2;");
    }

    [Fact]
    public async Task An_untracked_file_without_a_trailing_newline_says_so_the_way_git_would()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("partial.txt", "no newline here");

        var diff = await _git.GetFileDiffAsync(
            new FileDiffQuery(fixture.Root, "partial.txt", Untracked: true),
            TestContext.Current.CancellationToken);

        diff.UnifiedDiff.ShouldNotBeNull().ShouldContain("\\ No newline at end of file");
    }

    [Fact]
    public async Task An_untracked_binary_file_is_reported_as_binary_rather_than_dumped()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteBinaryFile("new.bin", 2048);

        var diff = await _git.GetFileDiffAsync(
            new FileDiffQuery(fixture.Root, "new.bin", Untracked: true),
            TestContext.Current.CancellationToken);

        diff.Kind.ShouldBe(FileContentKind.Binary);
        diff.UnifiedDiff.ShouldBeNull();
        diff.SizeBytes.ShouldBe(2048);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("nested/../../outside.txt")]
    [InlineData("./relative.txt")]
    public async Task A_path_that_tries_to_leave_the_repository_is_refused(string path)
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        // Iteration 5 sandboxes the toolbox properly. This is the narrower promise that the Git
        // layer itself never reaches outside the repository it was handed, closed before
        // anything is built on top of it.
        var thrown = await Should.ThrowAsync<GitClientException>(
            () => _git.GetFileContentAsync(
                new FileContentQuery(fixture.Root, path, FileSide.WorkingTree),
                TestContext.Current.CancellationToken));

        thrown.Failure.ShouldBe(GitClientFailure.RepositoryUnreadable);
    }

    [Fact]
    public async Task An_absolute_path_is_refused()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        var absolute = OperatingSystem.IsWindows() ? @"C:\Windows\win.ini" : "/etc/passwd";

        await Should.ThrowAsync<GitClientException>(
            () => _git.GetFileContentAsync(
                new FileContentQuery(fixture.Root, absolute, FileSide.WorkingTree),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_repository_with_no_commits_has_no_head_side_for_anything()
    {
        using var fixture = FixtureRepository.CreateWithoutCommits();

        fixture.WriteFile("first.cs", "class First;\n");

        var content = await _git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "first.cs", FileSide.Head),
            TestContext.Current.CancellationToken);

        content.Kind.ShouldBe(FileContentKind.Absent);
    }

    [Fact]
    public void The_decoder_treats_content_with_a_nul_byte_as_binary()
    {
        // Git's own heuristic. A file this layer calls text and git calls binary would carry
        // line counts nothing else in the application agrees with.
        TextDecoding.LooksBinary(Encoding.ASCII.GetBytes("plain text")).ShouldBeFalse();
        TextDecoding.LooksBinary([0x41, 0x00, 0x42]).ShouldBeTrue();
    }
}
