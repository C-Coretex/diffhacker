using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// <c>list_changed_files</c>, <c>get_file_diff</c> and <c>get_path_info</c> — the tools that
/// describe the change under review.
/// </summary>
public sealed class ChangesetToolTests
{
    private static FixtureRepository Changed()
    {
        var repository = FixtureRepository.CreateWithCommit();

        repository.WriteFile("src/app.ts", "const a = 1;\n");
        repository.WriteFile("src/old.ts", "const old = 1;\n");
        repository.WriteFile("docs/guide.md", "# Guide\n");
        repository.Stage("src/app.ts", "src/old.ts", "docs/guide.md");
        repository.Commit("baseline");

        repository.WriteFile("src/app.ts", "const a = 1;\nconst b = 2;\n");
        repository.Delete("src/old.ts");
        repository.WriteFile("src/new.ts", "export const fresh = true;\n");

        return repository;
    }

    [Fact]
    public async Task Lists_every_changed_file_with_status_and_counts()
    {
        using var repository = Changed();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("list_changed_files");

        result.ShouldContain("src/app.ts");
        result.ShouldContain("src/old.ts");
        result.ShouldContain("src/new.ts");
        result.ShouldContain("[untracked]");
        result.ShouldContain("columns:");

        // §0.2.5: nothing is dropped, so the header's total is the real total.
        result.ShouldContain("whole change: 3 files");
    }

    [Fact]
    public async Task Filters_combine_with_and()
    {
        using var repository = Changed();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var typescript = await toolbox.CallAsync("list_changed_files", new { pathGlob = "src/**/*.ts" });
        typescript.ShouldContain("src/app.ts");
        typescript.ShouldNotContain("docs/");

        var deleted = await toolbox.CallAsync("list_changed_files", new { status = "deleted" });
        deleted.ShouldContain("src/old.ts");
        deleted.ShouldNotContain("src/new.ts");

        var impossible = await toolbox.CallAsync("list_changed_files", new { status = "deleted", pathGlob = "docs/**" });
        impossible.ShouldContain("No changed file matches those filters");
    }

    [Fact]
    public async Task Rejects_a_status_that_is_not_one_and_says_which_are()
    {
        using var repository = Changed();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("list_changed_files", new { status = "changed" });

        result.ShouldContain("is not a status");
        result.ShouldContain("added, modified, deleted, renamed, copied");
    }

    [Fact]
    public async Task Says_plainly_when_the_working_tree_is_clean()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("list_changed_files");

        result.ShouldContain("working tree is clean");
        result.ShouldContain("no change to review");
    }

    [Fact]
    public async Task Returns_diffs_for_several_files_in_one_call()
    {
        using var repository = Changed();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("get_file_diff", new { paths = new[] { "src/app.ts", "src/new.ts" } });

        result.ShouldContain("--- src/app.ts (M) ---");
        result.ShouldContain("+const b = 2;");

        // An untracked file has no HEAD side, so its diff is synthesised from the file itself.
        result.ShouldContain("--- src/new.ts (A) ---");
        result.ShouldContain("+export const fresh = true;");
    }

    [Fact]
    public async Task Points_an_unchanged_file_at_read_file_instead_of_failing()
    {
        using var repository = Changed();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("get_file_diff", new { paths = new[] { "docs/guide.md" } });

        result.ShouldContain("did not change");
        result.ShouldContain("read_file");
    }

    [Fact]
    public async Task Refuses_more_diffs_than_one_call_allows()
    {
        using var repository = Changed();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var paths = Enumerable.Range(0, 40).Select(n => $"f{n}.txt").ToArray();
        var result = await toolbox.CallAsync("get_file_diff", new { paths });

        result.ShouldContain("Too many paths: 40");
        result.ShouldContain("at most 10");
    }

    [Fact]
    public async Task Reports_a_binary_diff_as_a_size_rather_than_bytes()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteBinaryFile("image.png", 2048);
        repository.Stage("image.png");
        repository.Commit("add image");
        repository.WriteBinaryFile("image.png", 4096);

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("get_file_diff", new { paths = new[] { "image.png" } });

        result.ShouldContain("Binary file");
        result.ShouldNotContain("\0");
    }

    [Fact]
    public async Task Describes_a_path_without_reading_it()
    {
        using var repository = Changed();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("get_path_info", new
        {
            paths = new[] { "src/app.ts", "docs/guide.md" },
        });

        result.ShouldContain("src/app.ts");
        result.ShouldContain("language=TypeScript");
        result.ShouldContain("changed=M");
        result.ShouldContain("docs/guide.md");
        result.ShouldContain("unchanged");

        // "Without reading it" is the point: no file content appears.
        result.ShouldNotContain("const a = 1;");
    }

    [Fact]
    public async Task A_renamed_file_says_what_it_used_to_be()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("before.md", "content that is long enough for rename detection to be sure\n");
        repository.Stage("before.md");
        repository.Commit("add");
        repository.Rename("before.md", "after.md");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("list_changed_files");

        result.ShouldContain("after.md");
        result.ShouldContain("(was before.md)");
    }

    [Fact]
    public async Task Refresh_picks_up_a_file_written_after_the_snapshot()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        (await toolbox.CallAsync("list_changed_files")).ShouldContain("working tree is clean");

        repository.WriteFile("late.txt", "written after the snapshot\n");

        // Still clean: the snapshot is deliberately stable across calls.
        (await toolbox.CallAsync("list_changed_files")).ShouldContain("working tree is clean");

        var refreshed = await toolbox.CallAsync("list_changed_files", new { refresh = true });
        refreshed.ShouldContain("late.txt");
    }
}
