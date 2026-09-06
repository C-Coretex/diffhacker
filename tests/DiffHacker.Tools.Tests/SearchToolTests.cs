using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// <c>search_text</c>. The tool the iteration warns about most: a weak grep shows up later as a
/// bad graph and looks like a prompt problem.
/// </summary>
public sealed class SearchToolTests
{
    private static FixtureRepository Searchable()
    {
        var repository = FixtureRepository.CreateWithCommit();

        repository.WriteFile("src/auth.ts", "export function signIn(user: string) {\n  return token(user);\n}\n");
        repository.WriteFile("src/token.ts", "export function token(user: string) {\n  return user + '!';\n}\n");
        repository.WriteFile("docs/auth.md", "Authentication calls signIn.\n");
        repository.Stage("src/auth.ts", "src/token.ts", "docs/auth.md");
        repository.Commit("baseline");

        return repository;
    }

    [Fact]
    public async Task Finds_matches_across_the_repository_with_context()
    {
        using var repository = Searchable();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("search_text", new { pattern = "signIn" });

        result.ShouldContain("src/auth.ts");
        result.ShouldContain("docs/auth.md");
        result.ShouldContain("in 2 file(s)");

        // git's own grep shape: ':' marks the match, '-' marks context.
        result.ShouldContain("     1: export function signIn");
        result.ShouldContain("     2-");
    }

    [Fact]
    public async Task Context_lines_are_configurable_and_zero_means_matches_only()
    {
        using var repository = Searchable();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var none = await toolbox.CallAsync("search_text", new { pattern = "signIn", contextLines = 0 });

        none.ShouldContain("signIn");
        none.ShouldNotContain("     2-");
    }

    [Fact]
    public async Task Fixed_mode_treats_regex_metacharacters_literally()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("code.ts", "const re = a.b(c);\nconst other = axbxc;\n");
        repository.Stage("code.ts");
        repository.Commit("add");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        // contextLines 0 so the assertion is about what matched, not about what happens to sit
        // next to it: with context on, the second line shows up as context and rightly so.
        var literal = await toolbox.CallAsync("search_text", new
        {
            pattern = "a.b(c)",
            mode = "fixed",
            contextLines = 0,
        });

        literal.ShouldContain("const re = a.b(c);");
        literal.ShouldContain("1 match(es)");
        literal.ShouldNotContain("axbxc");
    }

    [Fact]
    public async Task Extended_mode_runs_a_posix_regex()
    {
        using var repository = Searchable();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("search_text", new
        {
            pattern = "^export function (signIn|token)",
            mode = "extended",
        });

        result.ShouldContain("src/auth.ts");
        result.ShouldContain("src/token.ts");
    }

    [Fact]
    public async Task A_pattern_git_rejects_comes_back_as_advice_not_a_crash()
    {
        using var repository = Searchable();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.InvokeAsync("search_text", new { pattern = "[unterminated", mode = "extended" });

        result.IsError.ShouldBeFalse("a bad pattern is something the model should correct, not a failed call");
        result.Content.ShouldContain("rejected that pattern");
        result.Content.ShouldContain("mode='fixed'");
    }

    [Fact]
    public async Task Case_insensitive_search_is_available()
    {
        using var repository = Searchable();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        (await toolbox.CallAsync("search_text", new { pattern = "SIGNIN" }))
            .ShouldContain("No match");

        (await toolbox.CallAsync("search_text", new { pattern = "SIGNIN", caseSensitive = false }))
            .ShouldContain("src/auth.ts");
    }

    [Fact]
    public async Task Restricts_to_a_path_glob()
    {
        using var repository = Searchable();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("search_text", new { pattern = "signIn", pathGlob = "docs/**" });

        result.ShouldContain("docs/auth.md");
        result.ShouldNotContain("src/auth.ts");
    }

    [Fact]
    public async Task Restricts_to_changed_files()
    {
        using var repository = Searchable();
        repository.WriteFile("src/auth.ts", "export function signIn(user: string) {\n  return token(user) + 'v2';\n}\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("search_text", new { pattern = "signIn", changedOnly = true });

        result.ShouldContain("src/auth.ts");
        result.ShouldContain("changed files only");
        result.ShouldNotContain("docs/auth.md");
    }

    [Fact]
    public async Task Skips_binary_files_rather_than_returning_their_bytes()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("plain.txt", "needle here\n");
        repository.WriteBytes("blob.bin", [.. "needle"u8.ToArray(), 0x00, 0x01, 0x02, .. "\n"u8.ToArray()]);
        repository.Stage("plain.txt", "blob.bin");
        repository.Commit("add both");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("search_text", new { pattern = "needle" });

        result.ShouldContain("plain.txt");
        result.ShouldNotContain("blob.bin");
    }

    [Fact]
    public async Task Never_searches_a_gitignored_file()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteGitignore("vendor/");
        repository.WriteFile("vendor/lib.js", "the needle is in here\n");
        repository.WriteFile("src/app.js", "no match in this one\n");
        repository.Stage(".gitignore", "src/app.js");
        repository.Commit("add");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("search_text", new { pattern = "needle" });

        result.ShouldContain("No match");
        result.ShouldNotContain("vendor");
    }

    [Fact]
    public async Task Searches_untracked_files_because_they_are_part_of_the_change()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("brand-new.ts", "export const needle = 1;\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("search_text", new { pattern = "needle" });

        result.ShouldContain("brand-new.ts");
    }

    [Fact]
    public async Task States_the_true_total_even_when_only_a_page_is_shown()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        for (var i = 0; i < 30; i++)
        {
            repository.WriteFile($"file{i}.txt", "needle\n");
        }

        repository.Stage(Enumerable.Range(0, 30).Select(i => $"file{i}.txt").ToArray());
        repository.Commit("many needles");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("search_text", new { pattern = "needle", limit = 5 });

        result.ShouldContain("30 match(es)");
        result.ShouldContain("truncated");
    }
}
