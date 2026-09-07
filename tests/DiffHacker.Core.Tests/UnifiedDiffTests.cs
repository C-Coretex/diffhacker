using DiffHacker.Core.Knowledge;

namespace DiffHacker.Core.Tests;

/// <summary>
/// The diff shown before an existing documentation file is replaced. It has one job — telling a
/// person what an overwrite would do — so what is tested is that it says the truth, not that it
/// produces the minimal edit script.
/// </summary>
public sealed class UnifiedDiffTests
{
    [Fact]
    public void Identical_text_has_nothing_to_show()
    {
        UnifiedDiff.Compute("one\ntwo\n", "one\ntwo\n", "A.md").ShouldBeNull();
    }

    [Fact]
    public void A_trailing_newline_on_one_side_only_is_not_a_change_worth_showing()
    {
        // A file and the same file with its final newline present differ by nothing a reader can
        // see, and reporting it would make every export look like an overwrite.
        UnifiedDiff.Compute("one\ntwo", "one\ntwo\n", "A.md").ShouldBeNull();
    }

    [Fact]
    public void A_replaced_line_shows_as_a_removal_and_an_addition()
    {
        var diff = UnifiedDiff.Compute("one\ntwo\nthree\n", "one\nTWO\nthree\n", "A.md").ShouldNotBeNull();

        diff.ShouldContain("--- a/A.md");
        diff.ShouldContain("+++ b/A.md");
        diff.ShouldContain("-two");
        diff.ShouldContain("+TWO");
        diff.ShouldContain(" one");
        diff.ShouldContain(" three");
    }

    [Fact]
    public void An_added_line_shows_only_as_an_addition()
    {
        var diff = UnifiedDiff.Compute("one\n", "one\ntwo\n", "A.md").ShouldNotBeNull();

        diff.ShouldContain("+two");
        diff.ShouldNotContain("-one");
    }

    [Fact]
    public void Writing_over_an_empty_file_shows_every_line_as_added()
    {
        var diff = UnifiedDiff.Compute(string.Empty, "one\ntwo\n", "A.md").ShouldNotBeNull();

        diff.ShouldContain("+one");
        diff.ShouldContain("+two");
    }

    [Fact]
    public void Distant_changes_are_reported_as_separate_hunks()
    {
        var before = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"line {i}"));
        var after = before.Replace("line 2\n", "CHANGED 2\n", StringComparison.Ordinal)
            .Replace("line 38", "CHANGED 38", StringComparison.Ordinal);

        var diff = UnifiedDiff.Compute(before, after, "A.md").ShouldNotBeNull();

        diff.Split('\n').Count(line => line.StartsWith("@@", StringComparison.Ordinal)).ShouldBe(2);

        // And the thirty lines nobody touched are not in it.
        diff.ShouldNotContain("line 20");
    }

    [Fact]
    public void Windows_line_endings_do_not_read_as_a_change_on_every_line()
    {
        UnifiedDiff.Compute("one\r\ntwo\r\n", "one\ntwo\n", "A.md").ShouldBeNull();
    }

    [Fact]
    public void Hunk_headers_count_the_lines_they_contain()
    {
        var diff = UnifiedDiff.Compute("a\nb\nc\n", "a\nx\nc\n", "A.md").ShouldNotBeNull();

        // Three context/removed lines on the left, three context/added on the right.
        diff.ShouldContain("@@ -1,3 +1,3 @@");
    }
}
