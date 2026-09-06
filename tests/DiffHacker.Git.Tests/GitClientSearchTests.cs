using DiffHacker.Core.Changes;
using DiffHacker.TestSupport;

namespace DiffHacker.Git.Tests;

/// <summary>
/// The two reads Iteration 5 added: the visible-file list and <c>git grep</c>.
/// <para>
/// The grep tests matter more than they look. <c>git grep -z</c> replaces <i>both</i> field
/// separators with NUL, not just the one after the path, and the parser is written against that
/// exact shape — so these pin the byte format the toolbox's whole search depends on. If a future
/// git changes it, this is where it should be noticed.
/// </para>
/// </summary>
public sealed class GitClientSearchTests
{
    private static GrepQuery Query(FixtureRepository repository, string pattern) => new()
    {
        RepositoryPath = repository.Root,
        Pattern = pattern,
        Syntax = GrepSyntax.Fixed,
    };

    [Fact]
    public async Task Lists_tracked_and_unignored_untracked_files_and_nothing_else()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteGitignore("ignored/");
        repository.WriteFile("tracked.txt", "t\n");
        repository.WriteFile("ignored/hidden.txt", "h\n");
        repository.Stage(".gitignore", "tracked.txt");
        repository.Commit("add");
        repository.WriteFile("untracked.txt", "u\n");

        var files = await GitClientFactory.Create()
            .ListFilesAsync(new FileListQuery(repository.Root), TestContext.Current.CancellationToken);

        files.ShouldContain("tracked.txt");
        files.ShouldContain("untracked.txt");
        files.ShouldContain("readme.md");
        files.ShouldContain(".gitignore");
        files.ShouldNotContain("ignored/hidden.txt");
    }

    [Fact]
    public async Task The_file_list_is_sorted_and_free_of_duplicates()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("b.txt", "b\n");
        repository.WriteFile("a.txt", "a\n");
        repository.Stage("a.txt", "b.txt");
        repository.Commit("add");

        // Modified but still tracked: --cached and --others must not report it twice.
        repository.WriteFile("a.txt", "a changed\n");

        var files = await GitClientFactory.Create()
            .ListFilesAsync(new FileListQuery(repository.Root), TestContext.Current.CancellationToken);

        files.ShouldBe(files.Order(StringComparer.Ordinal).ToArray());
        files.Distinct(StringComparer.Ordinal).Count().ShouldBe(files.Count);
    }

    [Fact]
    public async Task Finds_matches_with_their_path_line_number_and_text()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("src/one.txt", "alpha\nbeta needle here\ngamma\n");
        repository.Stage("src/one.txt");
        repository.Commit("add");

        var result = await GitClientFactory.Create()
            .GrepAsync(Query(repository, "needle"), TestContext.Current.CancellationToken);

        var match = result.Matches.ShouldHaveSingleItem();
        match.Path.ShouldBe("src/one.txt");
        match.LineNumber.ShouldBe(2);
        match.Line.ShouldBe("beta needle here");
        result.TotalMatches.ShouldBe(1);
        result.FileCount.ShouldBe(1);
        result.CountIsExact.ShouldBeTrue();
    }

    [Fact]
    public async Task A_line_containing_a_colon_is_parsed_correctly()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        // The parse anchors on NUL and a digit run. A line full of colons is what would break a
        // parser that split on ':' the way git's human-readable output invites.
        repository.WriteFile("conf.yml", "key: value\nneedle: a:b:c:12:34\n");
        repository.Stage("conf.yml");
        repository.Commit("add");

        var result = await GitClientFactory.Create()
            .GrepAsync(Query(repository, "needle"), TestContext.Current.CancellationToken);

        var match = result.Matches.ShouldHaveSingleItem();
        match.LineNumber.ShouldBe(2);
        match.Line.ShouldBe("needle: a:b:c:12:34");
    }

    [Fact]
    public async Task A_path_containing_spaces_and_non_ascii_survives()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("docs/a café file.md", "the needle\n");
        repository.Stage("docs/a café file.md");
        repository.Commit("add");

        var result = await GitClientFactory.Create()
            .GrepAsync(Query(repository, "needle"), TestContext.Current.CancellationToken);

        result.Matches.ShouldHaveSingleItem().Path.ShouldBe("docs/a café file.md");
    }

    [Fact]
    public async Task No_match_is_a_result_not_a_failure()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        var result = await GitClientFactory.Create()
            .GrepAsync(Query(repository, "nothing matches this"), TestContext.Current.CancellationToken);

        // git exits 1 for "no matches", which is not an error condition.
        result.Matches.ShouldBeEmpty();
        result.TotalMatches.ShouldBe(0);
        result.PatternError.ShouldBeNull();
    }

    [Fact]
    public async Task Counts_past_the_page_so_the_caller_can_state_a_true_total()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        for (var i = 0; i < 25; i++)
        {
            repository.WriteFile($"f{i:D2}.txt", "needle\n");
        }

        repository.Stage(Enumerable.Range(0, 25).Select(i => $"f{i:D2}.txt").ToArray());
        repository.Commit("add");

        var result = await GitClientFactory.Create().GrepAsync(
            Query(repository, "needle") with { Take = 5 },
            TestContext.Current.CancellationToken);

        result.Matches.Count.ShouldBe(5);
        result.TotalMatches.ShouldBe(25);
        result.FileCount.ShouldBe(25);
        result.CountIsExact.ShouldBeTrue();
    }

    [Fact]
    public async Task Skip_and_take_page_through_matches_without_gaps_or_repeats()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        for (var i = 0; i < 20; i++)
        {
            repository.WriteFile($"f{i:D2}.txt", "needle\n");
        }

        repository.Stage(Enumerable.Range(0, 20).Select(i => $"f{i:D2}.txt").ToArray());
        repository.Commit("add");

        var client = GitClientFactory.Create();
        var seen = new List<string>();

        for (var skip = 0; skip < 20; skip += 6)
        {
            var page = await client.GrepAsync(
                Query(repository, "needle") with { Skip = skip, Take = 6 },
                TestContext.Current.CancellationToken);

            seen.AddRange(page.Matches.Select(match => match.Path));
        }

        seen.Count.ShouldBe(20);
        seen.Distinct(StringComparer.Ordinal).Count().ShouldBe(20);
    }

    [Fact]
    public async Task The_scan_ceiling_stops_counting_and_says_the_total_is_a_lower_bound()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("many.txt", string.Join('\n', Enumerable.Repeat("needle", 200)));
        repository.Stage("many.txt");
        repository.Commit("add");

        var result = await GitClientFactory.Create().GrepAsync(
            Query(repository, "needle") with { Take = 5, ScanCeiling = 50 },
            TestContext.Current.CancellationToken);

        result.CountIsExact.ShouldBeFalse();
        result.TotalMatches.ShouldBe(50);
    }

    [Fact]
    public async Task Binary_files_are_skipped()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteBytes("blob.bin", [.. "needle"u8.ToArray(), 0x00, 0x01, .. "\n"u8.ToArray()]);
        repository.Stage("blob.bin");
        repository.Commit("add");

        var result = await GitClientFactory.Create()
            .GrepAsync(Query(repository, "needle"), TestContext.Current.CancellationToken);

        result.Matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_untracked_file_is_searched_because_it_is_part_of_the_change()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("brand-new.txt", "the needle\n");

        var result = await GitClientFactory.Create()
            .GrepAsync(Query(repository, "needle"), TestContext.Current.CancellationToken);

        result.Matches.ShouldHaveSingleItem().Path.ShouldBe("brand-new.txt");
    }

    [Fact]
    public async Task A_gitignored_file_is_never_searched()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteGitignore("vendor/");
        repository.WriteFile("vendor/lib.js", "the needle\n");
        repository.Stage(".gitignore");
        repository.Commit("add");

        var result = await GitClientFactory.Create()
            .GrepAsync(Query(repository, "needle"), TestContext.Current.CancellationToken);

        result.Matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pattern_git_rejects_comes_back_as_an_error_string_not_an_exception()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        var result = await GitClientFactory.Create().GrepAsync(
            Query(repository, "[unterminated") with { Syntax = GrepSyntax.Extended },
            TestContext.Current.CancellationToken);

        result.PatternError.ShouldNotBeNullOrWhiteSpace();
        result.Matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Extended_and_fixed_syntaxes_differ_as_advertised()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("code.txt", "a.c\nabc\n");
        repository.Stage("code.txt");
        repository.Commit("add");

        var client = GitClientFactory.Create();

        var literal = await client.GrepAsync(
            Query(repository, "a.c") with { Syntax = GrepSyntax.Fixed },
            TestContext.Current.CancellationToken);

        literal.Matches.ShouldHaveSingleItem().Line.ShouldBe("a.c");

        var regex = await client.GrepAsync(
            Query(repository, "a.c") with { Syntax = GrepSyntax.Extended },
            TestContext.Current.CancellationToken);

        regex.Matches.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Perl_syntax_either_works_or_is_answered_in_extended_and_says_so()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("nums.txt", "value 42\nvalue x\n");
        repository.Stage("nums.txt");
        repository.Commit("add");

        var result = await GitClientFactory.Create().GrepAsync(
            Query(repository, "value [0-9]+") with { Syntax = GrepSyntax.Perl },
            TestContext.Current.CancellationToken);

        // Whichever way this git was built, the caller is told which dialect actually ran and
        // gets a usable answer rather than a failure.
        result.PatternError.ShouldBeNull();
        result.Matches.ShouldHaveSingleItem().Line.ShouldBe("value 42");
        result.SyntaxUsed.ShouldBeOneOf(GrepSyntax.Perl, GrepSyntax.Extended);
    }

    [Fact]
    public async Task Case_insensitivity_and_path_globs_are_honoured()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        repository.WriteFile("src/one.txt", "NEEDLE\n");
        repository.WriteFile("docs/two.txt", "needle\n");
        repository.Stage("src/one.txt", "docs/two.txt");
        repository.Commit("add");

        var client = GitClientFactory.Create();

        var sensitive = await client.GrepAsync(Query(repository, "needle"), TestContext.Current.CancellationToken);
        sensitive.Matches.ShouldHaveSingleItem().Path.ShouldBe("docs/two.txt");

        var insensitive = await client.GrepAsync(
            Query(repository, "needle") with { CaseSensitive = false },
            TestContext.Current.CancellationToken);

        insensitive.Matches.Count.ShouldBe(2);

        var scoped = await client.GrepAsync(
            Query(repository, "needle") with { CaseSensitive = false, PathGlob = "src/**" },
            TestContext.Current.CancellationToken);

        scoped.Matches.ShouldHaveSingleItem().Path.ShouldBe("src/one.txt");
    }

    [Fact]
    public async Task Searching_a_path_that_is_not_a_repository_fails_as_a_git_client_error()
    {
        using var directory = FixtureRepository.CreateEmptyDirectory();

        await Should.ThrowAsync<GitClientException>(async () =>
            await GitClientFactory.Create()
                .GrepAsync(Query(directory, "needle"), TestContext.Current.CancellationToken));
    }
}
