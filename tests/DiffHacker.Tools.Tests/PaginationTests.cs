using System.Text;
using System.Text.RegularExpressions;
using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// Requirement 3, and the iteration's verification step 5: results are capped, the truncation
/// marker is explicit, and the continuation token actually returns the next page.
/// <para>
/// The important test here is not that a page is short. It is that paging all the way through
/// reassembles into exactly the unpaged whole — a cursor that silently skipped or repeated rows
/// would pass every "is it truncated" assertion and still lose files, which §0.2.5 forbids.
/// </para>
/// </summary>
public sealed partial class PaginationTests
{
    [GeneratedRegex("cursor=\"([^\"]+)\"")]
    private static partial Regex CursorPattern();

    private static string? CursorOf(string result)
    {
        var match = CursorPattern().Match(result);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static FixtureRepository ManyFiles(int count)
    {
        var repository = FixtureRepository.CreateWithCommit();

        for (var i = 0; i < count; i++)
        {
            repository.WriteFile($"src/file{i:D4}.ts", $"export const value{i} = {i};\n");
        }

        return repository;
    }

    [Fact]
    public async Task Paging_changed_files_reassembles_into_the_whole_change()
    {
        using var repository = ManyFiles(120);
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await toolbox.CallAsync("list_changed_files", new { limit = 25, cursor });

            seen.AddRange(page
                .Split('\n')
                .Where(line => line.Contains("src/file", StringComparison.Ordinal))
                .Select(line => line[line.IndexOf("src/file", StringComparison.Ordinal)..].Trim())
                // Rows carry trailing flags such as [untracked]; the path is what is being counted.
                .Select(path => path.Split(' ')[0]));

            cursor = CursorOf(page);
            pages++;

            pages.ShouldBeLessThan(50, "paging must terminate");
        }
        while (cursor is not null);

        pages.ShouldBe(5);
        seen.Count.ShouldBe(120);
        seen.Distinct(StringComparer.Ordinal).Count().ShouldBe(120);
        seen.ShouldContain("src/file0000.ts");
        seen.ShouldContain("src/file0119.ts");
    }

    [Fact]
    public async Task Paging_find_files_reassembles_into_every_match()
    {
        using var repository = ManyFiles(60);
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var seen = new List<string>();
        string? cursor = null;

        do
        {
            var page = await toolbox.CallAsync("find_files", new { glob = "src/**/*.ts", limit = 10, cursor });

            seen.AddRange(page.Split('\n').Where(line => line.StartsWith("src/file", StringComparison.Ordinal)));
            cursor = CursorOf(page);
        }
        while (cursor is not null);

        seen.Count.ShouldBe(60);
        seen.Distinct(StringComparer.Ordinal).Count().ShouldBe(60);
    }

    [Fact]
    public async Task Paging_search_matches_reassembles_into_every_match()
    {
        using var repository = ManyFiles(50);
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var seen = new List<string>();
        string? cursor = null;

        do
        {
            var page = await toolbox.CallAsync("search_text", new
            {
                pattern = "export const",
                limit = 7,
                contextLines = 0,
                cursor,
            });

            seen.AddRange(page.Split('\n').Where(line => line.Contains(": export const", StringComparison.Ordinal)));
            cursor = CursorOf(page);
        }
        while (cursor is not null);

        seen.Count.ShouldBe(50);
    }

    [Fact]
    public async Task A_truncated_result_says_what_it_showed_out_of_what_total()
    {
        using var repository = ManyFiles(120);
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var page = await toolbox.CallAsync("list_changed_files", new { limit = 10 });

        page.ShouldContain("… truncated: showing 10 of 120 files.");
        page.ShouldContain("cursor=");
    }

    [Fact]
    public async Task An_untruncated_result_carries_no_marker_and_no_cursor()
    {
        using var repository = ManyFiles(3);
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var page = await toolbox.CallAsync("list_changed_files");

        page.ShouldNotContain("truncated");
        page.ShouldNotContain("cursor=");
    }

    [Fact]
    public async Task A_cursor_from_a_different_query_is_refused_rather_than_answered_wrongly()
    {
        using var repository = ManyFiles(60);
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var cursor = CursorOf(await toolbox.CallAsync("list_changed_files", new { limit = 10 }));
        cursor.ShouldNotBeNull();

        // Same tool, different filter: the page this cursor names is not the page it would land on.
        var wrong = await toolbox.CallAsync("list_changed_files", new
        {
            limit = 10,
            pathGlob = "src/file000*.ts",
            cursor,
        });

        wrong.ShouldContain("does not belong to this query");
    }

    [Fact]
    public async Task A_hand_written_cursor_is_refused()
    {
        using var repository = ManyFiles(20);
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("list_changed_files", new { limit = 5, cursor = "offset=10" });

        result.ShouldContain("does not belong to this query");
    }

    [Fact]
    public async Task A_page_size_beyond_the_maximum_is_clamped_rather_than_rejected()
    {
        using var repository = ManyFiles(600);
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var page = await toolbox.CallAsync("list_changed_files", new { limit = 100_000 });

        page.ShouldContain("changed files 1-500 of 600");
    }

    [Fact]
    public async Task No_result_exceeds_the_hard_byte_ceiling()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        // Rows long enough that the page size would blow the ceiling if only rows were counted.
        for (var i = 0; i < 500; i++)
        {
            repository.WriteFile($"src/{new string('d', 120)}/{i:D3}/deeply-named-source-file.ts", "x\n");
        }

        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var page = await toolbox.CallAsync("list_changed_files", new { limit = 500 });

        Encoding.UTF8.GetByteCount(page).ShouldBeLessThanOrEqualTo(ToolboxLimits.Default.MaxResultBytes);
        page.ShouldContain("truncated");
    }

    [Fact]
    public async Task The_header_counts_what_was_written_not_what_was_intended()
    {
        using var repository = ManyFiles(400);

        // A ceiling far below the page size, so the byte cap stops the body long before the page
        // does. The header is built after the body for exactly this case: "changed files 1-150"
        // printed above a footer saying 4 would be a contradiction a model resolves by trusting
        // whichever it read first.
        var limits = ToolboxLimits.Default with { MaxResultBytes = 900 };

        await using var toolbox = await ToolboxFixture.OpenAsync(
            repository,
            TestContext.Current.CancellationToken,
            limits);

        var page = await toolbox.CallAsync("list_changed_files", new { limit = 150 });

        var header = page.Split('\n')[0];
        var rows = page.Split('\n').Count(line => line.Contains("src/file", StringComparison.Ordinal));

        header.ShouldContain($"changed files 1-{rows} of 400");
        page.ShouldContain($"showing {rows} of 400 files");
    }

    [Fact]
    public async Task The_byte_ceiling_still_leaves_room_to_say_the_result_was_truncated()
    {
        using var repository = ManyFiles(400);

        // A deliberately tiny ceiling: the footer must survive even when the body cannot.
        var limits = ToolboxLimits.Default with { MaxResultBytes = 700 };

        await using var toolbox = await ToolboxFixture.OpenAsync(
            repository,
            TestContext.Current.CancellationToken,
            limits);

        var page = await toolbox.CallAsync("list_changed_files", new { limit = 400 });

        page.ShouldContain("truncated");
        page.ShouldContain("cursor=");
    }
}
