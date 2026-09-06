using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// <c>find_files</c>, <c>list_directory</c> and <c>get_repository_tree</c> — how a model finds
/// its way around a repository it has never seen.
/// </summary>
public sealed class DirectoryToolTests
{
    private static FixtureRepository Laidout()
    {
        var repository = FixtureRepository.CreateWithCommit();

        repository.WriteFile("src/app/main.ts", "main\n");
        repository.WriteFile("src/app/util.ts", "util\n");
        repository.WriteFile("src/app/deep/nested/thing.ts", "deep\n");
        repository.WriteFile("src/lib/helper.ts", "helper\n");
        repository.WriteFile("docs/readme.md", "docs\n");
        repository.WriteFile("Dockerfile", "FROM scratch\n");
        repository.Stage(
            "src/app/main.ts",
            "src/app/util.ts",
            "src/app/deep/nested/thing.ts",
            "src/lib/helper.ts",
            "docs/readme.md",
            "Dockerfile");
        repository.Commit("layout");

        return repository;
    }

    [Fact]
    public async Task Star_does_not_cross_a_directory_boundary_but_double_star_does()
    {
        using var repository = Laidout();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var shallow = await toolbox.CallAsync("find_files", new { glob = "src/app/*.ts" });
        shallow.ShouldContain("src/app/main.ts");
        shallow.ShouldNotContain("src/app/deep/nested/thing.ts");

        var deep = await toolbox.CallAsync("find_files", new { glob = "src/**/*.ts" });
        deep.ShouldContain("src/app/main.ts");
        deep.ShouldContain("src/app/deep/nested/thing.ts");
        deep.ShouldContain("src/lib/helper.ts");
    }

    [Fact]
    public async Task Double_star_also_matches_zero_directories()
    {
        using var repository = Laidout();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        // The most common globbing surprise: src/**/*.ts should find src/lib/helper.ts *and*
        // anything sitting directly in src/.
        var result = await toolbox.CallAsync("find_files", new { glob = "**/Dockerfile" });

        result.ShouldContain("Dockerfile");
    }

    [Fact]
    public async Task An_unusable_glob_is_explained_rather_than_returning_nothing()
    {
        using var repository = Laidout();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("find_files", new { glob = "src/[unterminated" });

        result.ShouldContain("not a usable glob");
    }

    [Fact]
    public async Task Find_files_can_be_limited_to_the_change()
    {
        using var repository = Laidout();
        repository.WriteFile("src/app/main.ts", "main v2\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var all = await toolbox.CallAsync("find_files", new { glob = "src/**/*.ts" });
        all.ShouldContain("src/lib/helper.ts");

        var changed = await toolbox.CallAsync("find_files", new { glob = "src/**/*.ts", changedOnly = true });
        changed.ShouldContain("src/app/main.ts");
        changed.ShouldNotContain("src/lib/helper.ts");
    }

    [Fact]
    public async Task Lists_a_directory_with_subdirectory_file_counts()
    {
        using var repository = Laidout();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("list_directory", new { path = "src" });

        result.ShouldContain("app/  (3 files)");
        result.ShouldContain("lib/  (1 files)");
        result.ShouldNotContain("docs");
    }

    [Fact]
    public async Task Lists_the_repository_root_when_given_no_path()
    {
        using var repository = Laidout();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("list_directory");

        result.ShouldContain("repository root");
        result.ShouldContain("Dockerfile");
        result.ShouldContain("src/");
    }

    [Fact]
    public async Task Marks_changed_files_in_a_listing()
    {
        using var repository = Laidout();
        repository.WriteFile("src/lib/helper.ts", "helper v2\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("list_directory", new { path = "src/lib" });

        result.ShouldContain("helper.ts  [M]");
    }

    [Fact]
    public async Task The_tree_stops_at_the_requested_depth_and_says_what_is_below()
    {
        using var repository = Laidout();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var shallow = await toolbox.CallAsync("get_repository_tree", new { maxDepth = 1 });
        shallow.ShouldContain("src/  (4 files)");
        shallow.ShouldNotContain("main.ts");

        // src > app > deep > nested > thing.ts, so depth 4 reaches the directory and stops,
        // still saying how many files are inside it rather than implying there are none.
        var partway = await toolbox.CallAsync("get_repository_tree", new { maxDepth = 4 });
        partway.ShouldContain("main.ts");
        partway.ShouldContain("nested/  (1 files)");
        partway.ShouldNotContain("thing.ts");

        var deeper = await toolbox.CallAsync("get_repository_tree", new { maxDepth = 5 });
        deeper.ShouldContain("thing.ts");
    }

    [Fact]
    public async Task The_tree_can_start_from_a_subdirectory()
    {
        using var repository = Laidout();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("get_repository_tree", new { path = "src/app", maxDepth = 3 });

        result.ShouldContain("main.ts");
        result.ShouldNotContain("docs");
    }

    [Fact]
    public async Task A_deep_tree_is_handled_without_blowing_up()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        var path = string.Join('/', Enumerable.Range(0, 40).Select(n => $"d{n}"));
        repository.WriteFile(path + "/leaf.txt", "leaf\n");
        repository.Stage(path + "/leaf.txt");
        repository.Commit("deep");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var capped = await toolbox.CallAsync("get_repository_tree", new { maxDepth = 100 });
        capped.ShouldContain("to depth 10");

        var found = await toolbox.CallAsync("find_files", new { glob = "**/leaf.txt" });
        found.ShouldContain("leaf.txt");
    }

    [Fact]
    public async Task Directories_holding_only_ignored_files_do_not_appear_as_empty_ones()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteGitignore("out/");
        repository.WriteFile("out/artifact.bin", "x");
        repository.WriteFile("kept.txt", "x\n");
        repository.Stage(".gitignore", "kept.txt");
        repository.Commit("add");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("get_repository_tree", new { maxDepth = 3 });

        result.ShouldNotContain("out/");
        result.ShouldContain("kept.txt");
    }
}
