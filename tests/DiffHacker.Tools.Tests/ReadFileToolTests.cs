using System.Text;
using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// <c>read_file</c>: the tool a model reaches for most, and the one where encoding, size and line
/// arithmetic all have to be right at once.
/// </summary>
public sealed class ReadFileToolTests
{
    [Fact]
    public async Task Reads_a_file_with_line_numbers()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("src/app.ts", "const a = 1;\nconst b = 2;\nconst c = 3;\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path = "src/app.ts" });

        result.ShouldContain("src/app.ts");
        result.ShouldContain("working tree");
        result.ShouldContain("lines 1-3 of 3");
        result.ShouldContain("const b = 2;");
        result.ShouldContain("     2  ");
    }

    [Fact]
    public async Task Reads_the_committed_side_of_a_changed_file()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("notes.md", "before\n");
        repository.Stage("notes.md");
        repository.Commit("add notes");
        repository.WriteFile("notes.md", "after\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var head = await toolbox.CallAsync("read_file", new { path = "notes.md", side = "head" });
        var working = await toolbox.CallAsync("read_file", new { path = "notes.md" });

        head.ShouldContain("before");
        head.ShouldNotContain("after");
        working.ShouldContain("after");
    }

    [Fact]
    public async Task Pages_a_long_file_and_says_where_to_continue()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("long.txt", string.Join('\n', Enumerable.Range(1, 1000).Select(n => $"line {n}")));

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var first = await toolbox.CallAsync("read_file", new { path = "long.txt", lineCount = 10 });

        first.ShouldContain("lines 1-10 of 1000");
        first.ShouldContain("line 10");
        first.ShouldNotContain("line 11\n");
        first.ShouldContain("startLine=11");

        var second = await toolbox.CallAsync("read_file", new { path = "long.txt", startLine = 11, lineCount = 10 });

        second.ShouldContain("lines 11-20 of 1000");
        second.ShouldContain("line 20");
    }

    [Fact]
    public async Task Reports_a_non_UTF8_file_rather_than_garbling_it()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        // é as a single Latin-1 byte, which is not valid UTF-8.
        repository.WriteBytes("latin.txt", [.. "caf"u8.ToArray(), 0xE9, .. "\n"u8.ToArray()]);

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path = "latin.txt" });

        result.ShouldContain("iso-8859-1");
        result.ShouldContain("not valid UTF-8");
        result.ShouldContain("café");
    }

    [Fact]
    public async Task Refuses_a_binary_file_with_its_size()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteBinaryFile("blob.bin", 4096);

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path = "blob.bin" });

        result.ShouldContain("binary file");
        result.ShouldContain("4 KB");
        result.ShouldNotContain("\0");
    }

    [Fact]
    public async Task Reports_a_file_too_large_to_read()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteLargeTextFile("huge.txt", 6L * 1024 * 1024);

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path = "huge.txt" });

        result.ShouldContain("larger than the 5 MB");
        result.ShouldContain("search_text");
    }

    [Fact]
    public async Task Says_when_a_line_range_starts_past_the_end()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("short.txt", "one\ntwo\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path = "short.txt", startLine = 500 });

        result.ShouldContain("has 2 lines");
        result.ShouldContain("past the end");
    }

    [Fact]
    public async Task Shortens_an_absurdly_long_line_rather_than_spending_the_whole_result_on_it()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("minified.js", new string('x', 40_000) + "\nafter\n");

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("read_file", new { path = "minified.js" });

        result.ShouldContain("more characters on this line");
        result.ShouldContain("after");
        Encoding.UTF8.GetByteCount(result).ShouldBeLessThan(ToolboxLimits.Default.MaxResultBytes);
    }
}
