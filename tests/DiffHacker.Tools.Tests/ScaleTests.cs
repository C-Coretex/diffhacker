using System.Text;
using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// Requirement 6: correct behaviour on very large files, deep trees, thousands of changed files
/// and non-UTF-8 encodings — plus the concurrency the LLM session actually subjects tools to.
/// </summary>
public sealed class ScaleTests
{
    [Fact]
    public async Task Fifteen_hundred_changed_files_are_all_reachable_and_none_are_dropped()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        for (var i = 0; i < 1500; i++)
        {
            repository.WriteFile($"src/mod{i / 100:D2}/file{i:D4}.ts", $"export const v{i} = {i};\n");
        }

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var first = await toolbox.CallAsync("list_changed_files", new { limit = 500 });

        // §0.2.5: every changed file appears. The header is the claim; paging proves it.
        first.ShouldContain("whole change: 1500 files");
        first.ShouldContain("changed files 1-500 of 1500");

        // And the result is still bounded, which is the whole point of the caps.
        Encoding.UTF8.GetByteCount(first).ShouldBeLessThanOrEqualTo(ToolboxLimits.Default.MaxResultBytes);

        var found = await toolbox.CallAsync("find_files", new { glob = "src/mod07/*.ts", limit = 1000 });
        found.ShouldContain("src/mod07/file0700.ts");
    }

    [Fact]
    public async Task Tools_are_safe_to_call_concurrently_because_the_session_dispatches_them_that_way()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        for (var i = 0; i < 40; i++)
        {
            repository.WriteFile($"src/file{i:D2}.ts", $"export const needle{i} = {i};\n");
        }

        repository.Stage(Enumerable.Range(0, 40).Select(i => $"src/file{i:D2}.ts").ToArray());
        repository.Commit("baseline");
        repository.WriteFile("src/file00.ts", "export const needle0 = 999;\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        // LlmSession.DispatchAsync runs every tool call in a turn through Task.WhenAll, so any
        // shared state here — the snapshot, ProjectLocator's cache — has to survive this.
        var calls = new List<Task<string>>();

        for (var i = 0; i < 6; i++)
        {
            calls.Add(toolbox.CallAsync("list_changed_files"));
            calls.Add(toolbox.CallAsync("search_text", new { pattern = "needle" }));
            calls.Add(toolbox.CallAsync("read_file", new { path = "src/file01.ts" }));
            calls.Add(toolbox.CallAsync("get_repository_tree", new { maxDepth = 3 }));
            calls.Add(toolbox.CallAsync("get_path_info", new { paths = new[] { "src/file02.ts" } }));
        }

        var results = await Task.WhenAll(calls);

        results.Length.ShouldBe(30);
        results.ShouldAllBe(result => result.Length > 0);
        results.ShouldAllBe(result => !result.Contains("The tool failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cancelled_call_stops_rather_than_returning_a_partial_answer()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        for (var i = 0; i < 200; i++)
        {
            repository.WriteFile($"src/file{i:D3}.ts", $"export const v{i} = {i};\n");
        }

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var tool = toolbox.Catalogue.LlmTools.Single(t => t.Name == "search_text");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await tool.Invoke("""{"pattern":"export"}""", cancelled.Token));
    }

    [Fact]
    public async Task A_file_whose_name_is_not_ascii_is_listed_and_readable()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("docs/café-naïve.md", "# Grüße\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        (await toolbox.CallAsync("list_changed_files")).ShouldContain("café-naïve.md");
        (await toolbox.CallAsync("read_file", new { path = "docs/café-naïve.md" })).ShouldContain("Grüße");
    }

    [Fact]
    public async Task A_utf16_file_is_reported_as_binary_rather_than_garbled()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteBytes("utf16.txt", [.. new byte[] { 0xFF, 0xFE }, .. Encoding.Unicode.GetBytes("hello utf16\n")]);

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path = "utf16.txt" });

        // Every other byte of UTF-16 text is NUL, and CLAUDE.md's settled encoding order sniffs
        // for NUL before it looks at the BOM — so this is binary, deliberately. Git decides the
        // same way, which is why `git diff` says "Binary files differ" for a UTF-16 file with no
        // working-tree-encoding attribute set.
        //
        // Requirement 6 asks for "readable or cleanly reported, never garbled silently". This is
        // the cleanly-reported half; ReadFileToolTests covers the readable half with Latin-1.
        result.ShouldContain("binary file");
        result.ShouldNotContain("\0");
        result.ShouldNotContain("h\0e\0l\0l\0o");
    }

    [Fact]
    public async Task An_empty_repository_answers_every_tool_without_failing()
    {
        using var repository = FixtureRepository.CreateWithoutCommits();
        repository.WriteFile("first.txt", "the very first file\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var changed = await toolbox.CallAsync("list_changed_files");
        changed.ShouldContain("no commits yet");
        changed.ShouldContain("first.txt");

        (await toolbox.CallAsync("get_repository_tree")).ShouldContain("first.txt");
        (await toolbox.CallAsync("read_file", new { path = "first.txt" })).ShouldContain("the very first file");
        (await toolbox.CallAsync("search_text", new { pattern = "first" })).ShouldContain("first.txt");
    }
}
