using DiffHacker.Core.Changes;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Git;

/// <summary>
/// The two reads Iteration 5's toolbox needs and the reviewing UI never did: what files exist,
/// and where a pattern occurs.
/// <para>
/// Both are deliberately here rather than in <c>DiffHacker.Tools</c>. Running git is what the
/// subcommand allowlist governs (§0.2.12), and the toolbox's promise is that it contains no way
/// to start a process at all — a promise that only means something if the toolbox has no
/// <c>Process</c> in it to audit.
/// </para>
/// </summary>
public sealed partial class GitClient
{
    /// <summary>
    /// Applied to every search. <c>-I</c> skips binary files, which a regex has nothing useful to
    /// say about; <c>--no-textconv</c> is the same guard the diff options carry, so a repository's
    /// own configuration cannot turn a search into command execution.
    /// </summary>
    private static readonly string[] CommonGrepOptions =
    [
        "--no-color",
        "--no-textconv",
        "-I",
        "-n",
        "-z",
        "--untracked",
    ];

    public async Task<IReadOnlyList<string>> ListFilesAsync(
        FileListQuery query,
        CancellationToken cancellationToken)
    {
        var root = await RequireRepositoryAsync(query.RepositoryPath, cancellationToken).ConfigureAwait(false);

        var paths = new HashSet<string>(StringComparer.Ordinal);

        var outcome = await runner.RunStreamingAsync(
            "ls-files",
            // --cached and --others together are "tracked, plus untracked"; --exclude-standard is
            // what removes everything .gitignore covers. One flag decides the toolbox's whole
            // field of view.
            ["--cached", "--others", "--exclude-standard", "-z"],
            root,
            async (stream, token) =>
            {
                var reader = new NulFieldReader(stream);
                while (await reader.ReadFieldAsync(token).ConfigureAwait(false) is { } path)
                {
                    // A nested repository is reported as a single entry with a trailing slash.
                    // It is a directory, not a file, and nothing in the toolbox can read it.
                    if (path.Length > 0 && !path.EndsWith('/'))
                    {
                        paths.Add(path);
                    }
                }
            },
            cancellationToken,
            timeout: GitProcessRunner.ChangesetTimeout).ConfigureAwait(false);

        RequireSuccess(outcome, "ls-files");

        var ordered = paths.ToArray();
        Array.Sort(ordered, StringComparer.Ordinal);

        ListedFiles(logger, ordered.Length, root);
        return ordered;
    }

    public async Task<GrepResult> GrepAsync(GrepQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrEmpty(query.Pattern);

        var root = await RequireRepositoryAsync(query.RepositoryPath, cancellationToken).ConfigureAwait(false);

        var attempt = await RunGrepAsync(root, query, query.Syntax, cancellationToken).ConfigureAwait(false);

        // Only Perl can be unavailable, and only because this git was built without PCRE. Answer
        // in extended syntax and say so rather than handing back a failure the model cannot fix.
        if (attempt.PcreUnavailable && query.Syntax is GrepSyntax.Perl)
        {
            PerlSyntaxUnavailable(logger);
            attempt = await RunGrepAsync(root, query, GrepSyntax.Extended, cancellationToken).ConfigureAwait(false);
        }

        return attempt.Result;
    }

    private async Task<(GrepResult Result, bool PcreUnavailable)> RunGrepAsync(
        string root,
        GrepQuery query,
        GrepSyntax syntax,
        CancellationToken cancellationToken)
    {
        // Capacity clamped, not taken from Take: a caller asking for "everything" would otherwise
        // have this try to reserve that many entries up front and throw before reading a byte.
        var kept = new List<GrepMatch>(Math.Clamp(query.Take, 0, 1024));
        var files = new HashSet<string>(StringComparer.Ordinal);
        var counted = 0;
        var hitCeiling = false;

        var outcome = await runner.RunStreamingAsync(
            "grep",
            BuildGrepArguments(query, syntax),
            root,
            async (stream, token) =>
            {
                var reader = new GitGrepReader(stream);

                while (await reader.ReadMatchAsync(token).ConfigureAwait(false) is { } match)
                {
                    if (counted >= query.ScanCeiling)
                    {
                        // Stop reading. RunStreamingAsync drains the rest and reaps the process,
                        // so leaving early here does not strand git on a full pipe.
                        hitCeiling = true;
                        return;
                    }

                    files.Add(match.Path);

                    if (counted >= query.Skip && kept.Count < query.Take)
                    {
                        kept.Add(match);
                    }

                    counted++;
                }
            },
            cancellationToken,
            timeout: GitProcessRunner.ChangesetTimeout).ConfigureAwait(false);

        if (outcome.CouldNotRun)
        {
            throw new GitClientException(
                outcome.TimedOutWaiting
                    ? "git grep did not finish in time and was stopped."
                    : "git could not be run for grep.",
                GitClientFailure.GitUnavailable);
        }

        // Exit 1 is git's way of saying "nothing matched". It is an answer, not a failure.
        var searched = outcome.ExitCode is 0 or 1;
        var stderr = outcome.StandardError.Trim();

        var result = new GrepResult
        {
            Matches = searched ? kept : [],
            TotalMatches = searched ? counted : 0,
            FileCount = searched ? files.Count : 0,
            CountIsExact = searched && !hitCeiling,
            SyntaxUsed = syntax,

            // Anything past exit 1 is git refusing the pattern — an unbalanced bracket, a
            // construct the chosen dialect does not have. Handed back as data so the model can
            // rewrite it, rather than thrown so the run ends.
            PatternError = searched ? null : stderr,
        };

        return (result, !searched && MentionsMissingPcre(stderr));
    }

    private static List<string> BuildGrepArguments(GrepQuery query, GrepSyntax syntax)
    {
        var arguments = new List<string>(CommonGrepOptions.Length + 8);
        arguments.AddRange(CommonGrepOptions);

        arguments.Add(syntax switch
        {
            GrepSyntax.Fixed => "-F",
            GrepSyntax.Perl => "-P",
            _ => "-E",
        });

        if (!query.CaseSensitive)
        {
            arguments.Add("-i");
        }

        // -e, always: without it a pattern that happens to start with a dash is read as an option.
        arguments.Add("-e");
        arguments.Add(query.Pattern);

        arguments.Add("--");

        if (!string.IsNullOrWhiteSpace(query.PathGlob))
        {
            // The explicit :(glob) magic, so ** means what the caller expects rather than
            // whatever the repository's pathspec defaults happen to be.
            arguments.Add(":(glob)" + query.PathGlob);
        }

        return arguments;
    }

    /// <summary>
    /// Whether git refused because it has no PCRE support, rather than because the pattern is bad.
    /// Matched on both spellings git has used for this message.
    /// </summary>
    private static bool MentionsMissingPcre(string standardError) =>
        standardError.Contains("PCRE", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("Perl-compatible", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(
        EventId = 2032,
        Level = LogLevel.Debug,
        Message = "Listed {FileCount} visible file(s) in {Repository}.")]
    private static partial void ListedFiles(ILogger logger, int fileCount, string repository);

    [LoggerMessage(
        EventId = 2033,
        Level = LogLevel.Information,
        Message = "This git has no PCRE support, so the search ran with extended regular expressions instead.")]
    private static partial void PerlSyntaxUnavailable(ILogger logger);
}
