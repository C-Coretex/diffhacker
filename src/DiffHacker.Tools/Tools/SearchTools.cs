using System.ComponentModel;
using System.Globalization;
using DiffHacker.Core.Changes;
using ModelContextProtocol.Server;

namespace DiffHacker.Tools.Tools;

/// <summary>
/// Repository-wide text search.
/// <para>
/// The engine is <c>git grep</c>, chosen during Iteration 5 planning over both an external tool
/// (an unlisted dependency, and the command execution requirement 4 forbids) and a .NET regex
/// pass (correct, but reimplementing the <c>.gitignore</c> traversal git already does).
/// </para>
/// <para>
/// Context lines are assembled here rather than asked of git, and that is not a preference. With
/// <c>-z</c> git replaces both field separators with NUL, which erases the <c>:</c>-versus-<c>-</c>
/// marker that distinguishes a matching line from a context line; without <c>-z</c> the paths
/// come back quoted and escaped, which CLAUDE.md forbids parsing. So git supplies match positions,
/// which it alone can determine, and the lines around them are read back from the files.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class SearchTools(RepositorySession session, IGitClient git, ToolboxLimits limits)
{
    /// <summary>
    /// How many raw matches a <c>changedOnly</c> search will hold in order to filter them.
    /// <para>
    /// Lower than the ordinary ceiling because these are kept in memory rather than counted and
    /// discarded. Twenty thousand matches is far past the point where a search is useful to a
    /// reader, and reaching it is reported rather than hidden.
    /// </para>
    /// </summary>
    private const int ChangedOnlyScanCeiling = 20_000;

    [McpServerTool(Name = "search_text", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Searches the text of every file git can see, and returns matches with surrounding lines.

        This is the main tool for exploring code you have not read. Use it to find where a symbol
        is defined, everywhere it is used, or which files mention a concept.

        Three pattern modes. 'fixed' matches the pattern literally and is what you want for a
        symbol name — no escaping to get wrong. 'extended' is POSIX extended regex: note that
        \\d and \\w are NOT available, write [0-9] and [A-Za-z0-9_]. 'perl' gives you the
        Perl-style syntax you are used to, but not every build of git has it; if this one does not,
        the search runs as 'extended' and the header says so.

        Binary files are skipped. Results are paged; the header always states the true total, so
        a match count is something you can reason about even when you only see the first page.
        """)]
    public async Task<string> SearchTextAsync(
        [Description("What to search for, in the dialect given by mode.")]
        string pattern,
        [Description("Pattern dialect: 'fixed' (literal, default), 'extended', or 'perl'.")]
        string mode = "fixed",
        [Description("Only files whose path matches this glob, e.g. 'src/**/*.ts'.")]
        string? pathGlob = null,
        [Description("Only files that are part of the change under review.")]
        bool changedOnly = false,
        [Description("Match case exactly. Defaults to true.")]
        bool caseSensitive = true,
        [Description("Lines of context each side of a match. Defaults to 2, capped at 8.")]
        int? contextLines = null,
        [Description("Continuation token from a previous truncated result.")]
        string? cursor = null,
        [Description("Matches per page. Defaults to 40, capped at 200.")]
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return "No pattern was given.";
        }

        var syntax = ParseSyntax(mode);
        if (syntax is null)
        {
            return $"'{mode}' is not a pattern mode. Use 'fixed', 'extended' or 'perl'.";
        }

        var pageSize = ToolboxLimits.Clamp(limit, limits.SearchMatches, limits.SearchMaxMatches);
        var context = Math.Clamp(contextLines ?? limits.SearchContextLines, 0, limits.SearchMaxContextLines);

        var fingerprint = Continuation.Describe(pattern, mode, pathGlob, changedOnly, caseSensitive, context);

        var offset = 0;
        if (cursor is not null && !Continuation.TryDecode(cursor, "search_text", fingerprint, out offset))
        {
            return Continuation.Mismatch;
        }

        // changedOnly is filtered here rather than passed to git as a pathspec: a 1500-file
        // changeset would be 1500 command-line arguments, which overruns the limit on Windows
        // long before it runs out of anything else. That means keeping every match to filter,
        // so the fetch is bounded — past ChangedOnlyScanCeiling the count is reported as a lower
        // bound rather than the answer silently becoming wrong.
        var result = await git.GrepAsync(
            new GrepQuery
            {
                RepositoryPath = session.Root,
                Pattern = pattern,
                Syntax = syntax.Value,
                CaseSensitive = caseSensitive,
                PathGlob = pathGlob,
                Skip = changedOnly ? 0 : offset,
                Take = changedOnly ? ChangedOnlyScanCeiling : pageSize,
                ScanCeiling = changedOnly ? ChangedOnlyScanCeiling : GrepQuery.DefaultScanCeiling,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.PatternError is { } error)
        {
            return $"git rejected that pattern in {mode} mode:\n{error}\n\n"
                + "If you meant it literally, use mode='fixed'. In 'extended' mode remember that "
                + "\\d and \\w do not exist — write [0-9] and [A-Za-z0-9_].";
        }

        var matches = (IReadOnlyList<GrepMatch>)result.Matches;
        var total = result.TotalMatches;

        if (changedOnly)
        {
            var filtered = result.Matches.Where(match => session.FindChanged(match.Path) is not null).ToArray();
            total = filtered.Length;
            matches = filtered.Skip(offset).Take(pageSize).ToArray();
        }

        if (total == 0)
        {
            var scope = (changedOnly ? ", changed files only" : string.Empty)
                + (pathGlob is null ? string.Empty : $", paths matching '{pathGlob}'");

            return $"No match for '{pattern}' ({result.SyntaxUsed.ToString().ToLowerInvariant()} mode{scope}).";
        }

        return await RenderAsync(
            pattern,
            result,
            matches,
            total,
            offset,
            pageSize,
            context,
            changedOnly,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RenderAsync(
        string pattern,
        GrepResult result,
        IReadOnlyList<GrepMatch> matches,
        int total,
        int offset,
        int pageSize,
        int context,
        bool changedOnly,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var syntaxNote = result.SyntaxUsed.ToString().ToLowerInvariant();
        var counted = result.CountIsExact ? total.ToString(CultureInfo.InvariantCulture) : "more than " + total;

        var scope = changedOnly ? " · changed files only" : string.Empty;
        var text = new ToolText(limits.MaxResultBytes);

        // One decoded copy per file, however many matches it holds. A file with forty hits in it
        // should cost one read, not forty.
        var cache = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.Ordinal);

        string? currentFile = null;
        var shown = 0;

        foreach (var match in matches.Take(pageSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(currentFile, match.Path, StringComparison.Ordinal))
            {
                if (currentFile is not null && !text.AddLine("--"))
                {
                    break;
                }

                if (!text.AddLine(match.Path))
                {
                    break;
                }

                currentFile = match.Path;
            }

            if (!AppendMatch(text, match, context, await LinesAsync(cache, match.Path, cancellationToken).ConfigureAwait(false)))
            {
                break;
            }

            shown++;
        }

        var delivered = offset + shown;

        // The header carries the total, which holds however much of the body fitted; how many
        // were actually shown is the footer's job. Written here rather than up front so the two
        // cannot contradict each other when the byte cap cuts the body short.
        var header = $"search '{pattern}' ({syntaxNote}) · {counted} match(es) in "
            + $"{result.FileCount} file(s){scope} · snapshot {ToolFormat.Timestamp(session.TakenAt)}";

        if (delivered < total)
        {
            return text.Render(header, ToolText.TruncationFooter(
                shown,
                total,
                result.CountIsExact,
                "matches",
                Continuation.Encode("search_text", fingerprint, delivered)));
        }

        return text.Render(header, result.CountIsExact
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Note: the search stopped counting at {total} matches. Narrow the pattern for an exact count."));
    }

    /// <summary>
    /// Writes one match with its context, in git's own grep shape: <c>:</c> marks the matching
    /// line, <c>-</c> marks context. Reconstructed here for the reason in the class remarks.
    /// </summary>
    private static bool AppendMatch(ToolText text, GrepMatch match, int context, IReadOnlyList<string>? lines)
    {
        if (lines is null || context == 0)
        {
            return text.AddLine(string.Create(CultureInfo.InvariantCulture, $"{match.LineNumber,6}: {match.Line}"));
        }

        var first = Math.Max(match.LineNumber - context, 1);
        var last = Math.Min(match.LineNumber + context, lines.Count);

        for (var number = first; number <= last; number++)
        {
            var marker = number == match.LineNumber ? ':' : '-';

            // Past the end of our copy — the file changed since git read it. The match line is
            // still worth showing, from git's own output.
            var body = number <= lines.Count ? lines[number - 1] : match.Line;

            if (!text.AddLine(string.Create(CultureInfo.InvariantCulture, $"{number,6}{marker} {body}")))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<IReadOnlyList<string>?> LinesAsync(
        Dictionary<string, IReadOnlyList<string>?> cache,
        string path,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var content = await git.GetFileContentAsync(
            new FileContentQuery(session.Root, path, FileSide.WorkingTree),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string>? lines = content.Kind is FileContentKind.Text && content.Text is { } body
            ? body.Split('\n').Select(line => line.TrimEnd('\r')).ToArray()
            : null;

        cache[path] = lines;
        return lines;
    }

    private static GrepSyntax? ParseSyntax(string mode) => mode?.ToLowerInvariant() switch
    {
        "fixed" or "literal" => GrepSyntax.Fixed,
        "extended" or "regex" => GrepSyntax.Extended,
        "perl" or "pcre" => GrepSyntax.Perl,
        _ => null,
    };
}
