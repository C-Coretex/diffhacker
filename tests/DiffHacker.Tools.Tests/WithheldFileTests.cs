using DiffHacker.Core.Knowledge;
using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// Files whose contents never reach a model.
/// <para>
/// The rule has two halves and both are load-bearing. Nothing may read, diff or search a withheld
/// file — that is the security half. And every listing must still show it — that is the honesty
/// half, and the reason this is not simply a filter: a changed <c>.env</c> is a real part of a
/// change, §0.2.5 says every changed file appears in the graph, and a reviewer who cannot see that
/// the file changed is worse off than one who can see it changed but not how.
/// </para>
/// </summary>
public sealed class WithheldFileTests
{
    [Fact]
    public async Task A_committed_env_file_cannot_be_read()
    {
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("read_file", new { path = ".env" });

        result.ShouldNotContain("hunter2");
        result.ShouldContain("withheld");
    }

    [Fact]
    public async Task A_private_key_anywhere_in_the_tree_cannot_be_read()
    {
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("read_file", new { path = "deploy/keys/server.pem" });

        result.ShouldNotContain("BEGIN PRIVATE KEY");
        result.ShouldContain("withheld");
    }

    [Fact]
    public async Task A_withheld_file_cannot_be_read_from_the_head_side_either()
    {
        // The rule is about the file, not about which side of the comparison is being asked for.
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("read_file", new { path = ".env", side = "head" });

        result.ShouldNotContain("hunter2");
        result.ShouldContain("withheld");
    }

    [Fact]
    public async Task A_changed_withheld_file_cannot_be_diffed()
    {
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("get_file_diff", new { paths = new[] { ".env" } });

        result.ShouldNotContain("rotated-secret");
        result.ShouldContain("withheld");
    }

    [Fact]
    public async Task A_withheld_file_still_appears_in_the_changed_file_list_and_is_flagged()
    {
        // §0.2.5: nothing is dropped from the change. Hiding it would be simpler and wrong.
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("list_changed_files");

        result.ShouldContain(".env");
        result.ShouldContain("[content withheld]");
    }

    [Fact]
    public async Task A_withheld_file_still_appears_in_a_directory_listing_and_is_flagged()
    {
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("list_directory");

        result.ShouldContain(".env");
        result.ShouldContain("[content withheld]");
    }

    [Fact]
    public async Task A_withheld_file_still_appears_in_the_repository_tree()
    {
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("get_repository_tree", new { maxDepth = 3 });

        result.ShouldContain(".env");
    }

    [Fact]
    public async Task A_withheld_file_is_findable_by_name()
    {
        // Knowing a repository has a .env is useful; reading it is not allowed. Those are
        // different questions and only the second is refused.
        await using var fixture = await OpenAsync();

        (await fixture.CallAsync("find_files", new { glob = "**/*.pem" }))
            .ShouldContain("deploy/keys/server.pem");
    }

    [Fact]
    public async Task Path_info_describes_a_withheld_file_without_opening_it()
    {
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("get_path_info", new { paths = new[] { ".env" } });

        result.ShouldContain(".env");
        result.ShouldContain("content withheld");
        result.ShouldNotContain("not found");
        result.ShouldNotContain("hunter2");
    }

    [Fact]
    public async Task A_search_returns_no_match_from_a_withheld_file()
    {
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("search_text", new { pattern = "hunter2" });

        result.ShouldNotContain("hunter2\n");
        result.ShouldContain("No match");
    }

    [Fact]
    public async Task A_search_that_matches_both_kinds_of_file_returns_only_the_readable_one()
    {
        await using var fixture = await OpenAsync();

        var result = await fixture.CallAsync("search_text", new { pattern = "TOKEN_NAME" });

        result.ShouldContain("src/config.ts");
        result.ShouldNotContain(".env");
    }

    [Fact]
    public async Task A_user_supplied_glob_withholds_a_file_the_built_in_list_does_not()
    {
        await using var fixture = await OpenAsync(["**/*.internal"]);

        var result = await fixture.CallAsync("read_file", new { path = "notes.internal" });

        result.ShouldNotContain("private thoughts");
        result.ShouldContain("withheld");
    }

    [Fact]
    public async Task With_no_globs_at_all_nothing_is_withheld()
    {
        // Proves the refusals above come from the rule rather than from something else going wrong.
        // The working-tree copy is the rotated one; the committed value is what "head" would show.
        await using var fixture = await OpenAsync([]);

        (await fixture.CallAsync("read_file", new { path = ".env" })).ShouldContain("rotated-secret");
    }

    [Fact]
    public async Task An_unusable_glob_is_ignored_rather_than_taking_the_toolbox_down()
    {
        // The list can contain something a user typed. One bad entry must not stop the built-in
        // entries applying.
        await using var fixture = await OpenAsync(["**/[unterminated", "**/.env"]);

        (await fixture.CallAsync("read_file", new { path = ".env" })).ShouldContain("withheld");
    }

    [Fact]
    public void The_default_list_covers_the_files_that_actually_hold_credentials()
    {
        SensitiveFiles.DefaultGlobs.ShouldContain("**/.env");
        SensitiveFiles.DefaultGlobs.ShouldContain("**/*.pem");
        SensitiveFiles.DefaultGlobs.ShouldContain("**/id_rsa*");
        SensitiveFiles.DefaultGlobs.ShouldContain("**/.npmrc");

        // A public certificate is public. Withholding it would cost a model context for nothing.
        SensitiveFiles.DefaultGlobs.ShouldNotContain("**/*.crt");
        SensitiveFiles.DefaultGlobs.ShouldNotContain("**/*.pub");
    }

    /// <summary>
    /// A repository with credentials committed to it, changed and unchanged, at the root and
    /// nested — every shape the rule has to survive.
    /// </summary>
    private static async Task<ToolboxFixture> OpenAsync(IReadOnlyList<string>? withheldGlobs = null)
    {
        var repository = FixtureRepository.CreateWithCommit();

        repository.WriteFile(".env", "API_TOKEN=hunter2\nTOKEN_NAME=api\n");
        repository.WriteFile("deploy/keys/server.pem", "-----BEGIN PRIVATE KEY-----\nsecret\n");
        repository.WriteFile("notes.internal", "private thoughts\n");
        repository.WriteFile("src/config.ts", "export const TOKEN_NAME = 'api';\n");
        repository.Stage(".");
        repository.Commit("Add configuration");

        // Changed after the commit, so the diff and the changed-file list both have it.
        repository.WriteFile(".env", "API_TOKEN=rotated-secret\nTOKEN_NAME=api\n");

        return await ToolboxFixture.OpenAsync(
            repository,
            TestContext.Current.CancellationToken,
            limits: null,
            withheldGlobs: withheldGlobs);
    }
}
