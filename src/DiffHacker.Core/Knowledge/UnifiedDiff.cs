using System.Globalization;
using System.Text;

namespace DiffHacker.Core.Knowledge;

/// <summary>
/// A line diff, for the one screen that has to show a change the git layer cannot produce.
/// <para>
/// Every other diff in this application comes from git, because every other diff is between two
/// things git knows about. The documentation export is not: it compares a file on disk against
/// text the application just generated and has not written anywhere, so there is nothing for git
/// to diff. Monaco's <c>DiffEditor</c> arrives in Iteration 10 and is not pulled forward for one
/// confirmation dialog, and a diff package is a dependency §0.4 would have to be asked about — so
/// this is the third option, which is sixty lines of Myers-free longest-common-subsequence.
/// </para>
/// <para>
/// It is used only to show a human what an overwrite would replace. Nothing depends on its output
/// being minimal, only on it being correct.
/// </para>
/// </summary>
public static class UnifiedDiff
{
    private const int ContextLines = 3;

    /// <summary>
    /// A unified diff from <paramref name="before"/> to <paramref name="after"/>, or null when
    /// the two are identical.
    /// </summary>
    public static string? Compute(string before, string after, string path)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var left = SplitLines(before);
        var right = SplitLines(after);
        var operations = Diff(left, right);

        // Judged on lines, not on bytes. Two texts that differ only in their line endings or in a
        // trailing newline differ by nothing a reader can see, and reporting that as a change
        // would make every export of unchanged documentation look like an overwrite.
        if (!operations.Any(operation => operation.Marker is not ' '))
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"--- a/{path}\n");
        builder.Append(CultureInfo.InvariantCulture, $"+++ b/{path}\n");

        foreach (var hunk in Hunks(operations))
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"@@ -{hunk.LeftStart},{hunk.LeftCount} +{hunk.RightStart},{hunk.RightCount} @@\n");

            foreach (var operation in hunk.Operations)
            {
                builder.Append(operation.Marker).Append(operation.Text).Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits on any line ending without keeping it, and without inventing a trailing empty line
    /// for text that ends in a newline — a file and the same file with its final newline present
    /// should differ by nothing the reader can see.
    /// </summary>
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not '\n')
            {
                continue;
            }

            var end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add(text[start..end]);
            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    /// <summary>
    /// Longest common subsequence over a dynamic-programming table, walked back into an ordered
    /// list of keep, remove and add operations.
    /// </summary>
    private static List<Operation> Diff(List<string> left, List<string> right)
    {
        var table = new int[left.Count + 1, right.Count + 1];

        for (var i = left.Count - 1; i >= 0; i--)
        {
            for (var j = right.Count - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var operations = new List<Operation>();
        var x = 0;
        var y = 0;

        while (x < left.Count && y < right.Count)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal))
            {
                operations.Add(new Operation(' ', left[x], x + 1, y + 1));
                x++;
                y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                operations.Add(new Operation('-', left[x], x + 1, null));
                x++;
            }
            else
            {
                operations.Add(new Operation('+', right[y], null, y + 1));
                y++;
            }
        }

        while (x < left.Count)
        {
            operations.Add(new Operation('-', left[x], x + 1, null));
            x++;
        }

        while (y < right.Count)
        {
            operations.Add(new Operation('+', right[y], null, y + 1));
            y++;
        }

        return operations;
    }

    /// <summary>Groups the operations into hunks with up to three lines of context each side.</summary>
    private static List<Hunk> Hunks(List<Operation> operations)
    {
        var hunks = new List<Hunk>();
        var index = 0;

        while (index < operations.Count)
        {
            if (operations[index].Marker == ' ')
            {
                index++;
                continue;
            }

            var start = Math.Max(0, index - ContextLines);
            var end = index;

            // Extend while the next change is close enough that the two hunks would overlap.
            while (end < operations.Count)
            {
                var next = NextChange(operations, end + 1);

                if (next is not null && next.Value - end <= ContextLines * 2)
                {
                    end = next.Value;
                    continue;
                }

                break;
            }

            end = Math.Min(operations.Count - 1, end + ContextLines);

            var slice = operations.GetRange(start, end - start + 1);
            hunks.Add(BuildHunk(slice));
            index = end + 1;
        }

        return hunks;
    }

    private static int? NextChange(List<Operation> operations, int from)
    {
        for (var i = from; i < operations.Count; i++)
        {
            if (operations[i].Marker != ' ')
            {
                return i;
            }
        }

        return null;
    }

    private static Hunk BuildHunk(List<Operation> slice)
    {
        var leftStart = slice.FirstOrDefault(operation => operation.LeftLine is not null)?.LeftLine ?? 0;
        var rightStart = slice.FirstOrDefault(operation => operation.RightLine is not null)?.RightLine ?? 0;

        return new Hunk(
            leftStart,
            slice.Count(operation => operation.Marker is ' ' or '-'),
            rightStart,
            slice.Count(operation => operation.Marker is ' ' or '+'),
            slice);
    }

    private sealed record Operation(char Marker, string Text, int? LeftLine, int? RightLine);

    private sealed record Hunk(
        int LeftStart,
        int LeftCount,
        int RightStart,
        int RightCount,
        IReadOnlyList<Operation> Operations);
}
