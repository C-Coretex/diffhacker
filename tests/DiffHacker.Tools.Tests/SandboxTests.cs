using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// Requirement 4 and the iteration's verification step 3: the toolbox cannot be talked out of the
/// repository it was given.
/// <para>
/// Every refusal is checked as a <i>result</i>, not an exception. A model that guesses a bad path
/// has to be able to read what went wrong and correct itself; an exception would end the run and
/// teach it nothing.
/// </para>
/// </summary>
public sealed class SandboxTests
{
    public static TheoryData<string> EscapeAttempts() =>
    [
        "../../etc/passwd",
        "../outside.txt",
        "src/../../outside.txt",
        "./secrets",
        "/etc/passwd",
        "C:\\Windows\\System32\\drivers\\etc\\hosts",
        "\\\\server\\share\\file.txt",
        "notes.txt:hidden",
    ];

    [Theory]
    [MemberData(nameof(EscapeAttempts))]
    public async Task Refuses_every_path_that_leaves_the_repository(string path)
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path });

        result.ShouldNotContain("fixture");
        result.ShouldSatisfyAllConditions(
            () => result.ShouldContain("not a repository-relative path", Case.Insensitive),
            () => result.ShouldNotBeEmpty());
    }

    public static TheoryData<string> GitInternals() =>
    [
        ".git/config",
        ".git/HEAD",
        ".git/objects/info/packs",
        ".GIT/config",
        "src/.git/config",
    ];

    [Theory]
    [MemberData(nameof(GitInternals))]
    public async Task Refuses_git_internals(string path)
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path });

        result.ShouldContain(".git/ are not readable");
    }

    [Fact]
    public async Task Refuses_a_symlink_that_points_outside_the_repository()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        var outside = Path.Combine(Path.GetTempPath(), $"diffhacker-outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "secret\n", TestContext.Current.CancellationToken);

        try
        {
            Assert.SkipUnless(
                repository.TryCreateSymlink("escape.txt", outside),
                "This platform will not create symlinks without elevation.");

            repository.Stage("escape.txt");

            await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

            var result = await toolbox.CallAsync("read_file", new { path = "escape.txt" });

            result.ShouldNotContain("secret");
            result.ShouldContain("outside the repository");
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Refuses_a_file_reached_through_a_directory_symlink_pointing_outside()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        var outside = Directory.CreateTempSubdirectory("diffhacker-outside-").FullName;
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "secret\n", TestContext.Current.CancellationToken);

        try
        {
            Assert.SkipUnless(
                repository.TryCreateDirectoryLink("vendor", outside),
                "This platform will not create a directory link.");

            await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

            // The leaf is not itself a link — the escape is an ancestor, which is exactly the
            // case a leaf-only check would wave through.
            var result = await toolbox.CallAsync("read_file", new { path = "vendor/secret.txt" });

            result.ShouldNotContain("secret\n");
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Refuses_a_gitignored_file_and_says_it_is_ignored_rather_than_absent()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteGitignore("secrets/");
        repository.WriteFile("secrets/key.txt", "hunter2\n");
        repository.Stage(".gitignore");
        repository.Commit("ignore secrets");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var read = await toolbox.CallAsync("read_file", new { path = "secrets/key.txt" });
        read.ShouldNotContain("hunter2");
        read.ShouldContain(".gitignore");

        // The distinction the whole visibility policy rests on: a model must be able to find out
        // that node_modules exists without being able to read it.
        var info = await toolbox.CallAsync("get_path_info", new { paths = new[] { "secrets/key.txt" } });
        info.ShouldContain("ignored by git");
        info.ShouldNotContain("not found");
    }

    [Fact]
    public async Task Counts_ignored_entries_rather_than_pretending_a_directory_is_empty()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteGitignore("build/");
        repository.WriteFile("build/one.o", "x");
        repository.WriteFile("build/two.o", "x");
        repository.WriteFile("src/main.c", "int main(void){return 0;}\n");
        repository.Stage(".gitignore", "src/main.c");
        repository.Commit("add source");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("list_directory");

        result.ShouldContain("src/");
        result.ShouldContain("ignored by git");
        result.ShouldNotContain("one.o");
    }

    [Fact]
    public async Task Refuses_a_path_that_does_not_exist_without_leaking_whether_a_sibling_does()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path = "nope/missing.txt" });

        result.ShouldContain("not a file git can see");
    }

    [Fact]
    public async Task The_repository_root_itself_is_listable_but_not_readable_as_a_file()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        (await toolbox.CallAsync("list_directory")).ShouldContain("readme.md");
        (await toolbox.CallAsync("read_file", new { path = "" })).ShouldContain("No path was given");
    }
}
