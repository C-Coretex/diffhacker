using System.Text;
using System.Text.RegularExpressions;

namespace DiffHacker.Tools;

/// <summary>
/// Path globbing, matching git's <c>:(glob)</c> pathspec closely enough that a pattern behaves
/// the same whether it went to <c>git grep</c> or was matched here.
/// <para>
/// <c>*</c> matches within one path segment, <c>**</c> crosses segments, <c>?</c> is one
/// character and <c>[abc]</c> is a class. That is git's rule, and it is also what anyone who has
/// used a build tool expects.
/// </para>
/// <para>
/// Compiled to a <see cref="Regex"/> with <see cref="RegexOptions.NonBacktracking"/>: the pattern
/// comes from a model, it gets applied to every path in the repository, and a linear-time engine
/// removes the possibility of one bad glob hanging a run. The translation below never emits a
/// backreference or a lookaround, so nothing here can be rejected by that engine.
/// </para>
/// </summary>
public static class Glob
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Compiles <paramref name="pattern"/>, or returns null when it is not a usable glob.
    /// </summary>
    public static Regex? TryCompile(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        try
        {
            return new Regex(
                Translate(pattern),
                RegexOptions.NonBacktracking | (RepositoryPathsAreCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
                MatchTimeout);
        }
        catch (ArgumentException)
        {
            // An unterminated character class, most likely. Not a usable glob.
            return null;
        }
    }

    /// <summary>
    /// Whether a path matches, with a null pattern meaning "everything" so callers can pass an
    /// optional filter straight through.
    /// </summary>
    public static bool Matches(Regex? compiled, string path)
    {
        if (compiled is null)
        {
            return true;
        }

        try
        {
            return compiled.IsMatch(path);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool RepositoryPathsAreCaseSensitive => OperatingSystem.IsLinux();

    private static string Translate(string pattern)
    {
        var builder = new StringBuilder("^");

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            switch (c)
            {
                case '*' when i + 1 < pattern.Length && pattern[i + 1] == '*':
                    i++;

                    // "**/" should also match zero directories, so that src/**/*.ts finds
                    // src/main.ts. Without this the pattern silently misses the top level, which
                    // is the single most common globbing surprise.
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                    {
                        i++;
                        builder.Append("(?:.*/)?");
                    }
                    else
                    {
                        builder.Append(".*");
                    }

                    break;

                case '*':
                    builder.Append("[^/]*");
                    break;

                case '?':
                    builder.Append("[^/]");
                    break;

                case '[':
                    i = AppendClass(pattern, i, builder);
                    break;

                default:
                    builder.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        return builder.Append('$').ToString();
    }

    /// <summary>Copies a character class across verbatim, translating a leading <c>!</c>.</summary>
    private static int AppendClass(string pattern, int start, StringBuilder builder)
    {
        var end = pattern.IndexOf(']', start + 1);

        if (end < 0)
        {
            // A shell would treat the unmatched bracket as a literal and quietly match nothing.
            // Here the pattern was written by a model, so an unbalanced class is far more likely
            // to be a mistake than an intent — and "that is not a usable glob" is something it
            // can act on, where an empty result looks like a fact about the repository.
            throw new ArgumentException($"'{pattern}' has an unterminated character class.", nameof(pattern));
        }

        var body = pattern[(start + 1)..end];
        builder.Append('[');

        if (body.StartsWith('!'))
        {
            builder.Append('^').Append(body[1..]);
        }
        else
        {
            builder.Append(body);
        }

        builder.Append(']');
        return end;
    }
}
