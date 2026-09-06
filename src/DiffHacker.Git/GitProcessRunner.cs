using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Git;

/// <summary>
/// Runs the git command line, and refuses to run anything that could change a repository.
/// <para>
/// CLAUDE.md §0.2.12 makes the application read-only. That is enforced here, by an allowlist
/// of subcommands rather than a denylist of dangerous ones: a denylist is wrong the moment git
/// grows a new subcommand, an allowlist is merely incomplete.
/// </para>
/// <para>
/// Two entry points. <see cref="RunAsync"/> captures both streams as strings and suits the small
/// metadata queries. <see cref="RunStreamingAsync"/> hands the caller the raw stdout
/// <see cref="Stream"/> and never buffers it, which is what Iteration 3 requirement 8 needs: a
/// 1500-file <c>git diff</c> must be consumed as it arrives, not materialised.
/// </para>
/// </summary>
/// <param name="logger">Records why git could not be run, when it cannot.</param>
/// <param name="executable">
/// Resolved through <c>PATH</c>. Overridable only so tests can point at a name that certainly
/// does not exist and exercise the git-is-missing path without touching the environment of the
/// machine running them.
/// </param>
public sealed partial class GitProcessRunner(ILogger<GitProcessRunner> logger, string executable = "git")
{
    /// <summary>
    /// Every subcommand this application may ever invoke. Read-only, all of them. Adding one
    /// that writes to the repository is a change to the product's contract, not a detail.
    /// <para>
    /// Note what is <b>not</b> here. <c>submodule</c> would be convenient for submodule metadata,
    /// but the allowlist grants a whole subcommand, so allowing it would also allow
    /// <c>submodule update</c>; the same metadata comes out of <c>diff --raw</c> mode bits
    /// instead. <c>hash-object</c> is read-only until someone passes <c>-w</c>, which is reason
    /// enough to leave it out.
    /// </para>
    /// <para>
    /// <c>grep</c> arrived in Iteration 5, and by the rule above that is a change to the contract
    /// rather than a detail. It earns the place: unlike <c>submodule</c> there is no mutating
    /// <c>grep</c> to come along with it, and the alternatives were shelling out to an external
    /// search tool — exactly the command execution the toolbox is forbidden — or reimplementing
    /// repository-wide search over <c>.gitignore</c> semantics git already knows. It starts no
    /// program of its own so long as <c>--no-textconv</c> is passed, which
    /// <see cref="GitClient"/> does.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> PermittedSubcommands =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "version",
            "rev-parse",
            "diff",
            "ls-files",
            "cat-file",
            "grep",
        };

    /// <summary>Long enough for metadata queries on a large repository, short enough to notice a hang.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// For the whole-changeset passes. A cold 1500-file diff on a large repository over a network
    /// filesystem genuinely takes minutes, and timing that out would be a bug, not a safeguard.
    /// </summary>
    public static readonly TimeSpan ChangesetTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Cap on captured stderr. Git's diagnostics are short; a runaway stream is not worth keeping.</summary>
    private const int MaxStandardErrorBytes = 8 * 1024;

    /// <summary>
    /// Runs <c>git &lt;subcommand&gt; &lt;arguments&gt;</c> and captures its streams as text.
    /// </summary>
    /// <exception cref="ArgumentException">The subcommand is not on the allowlist.</exception>
    public async Task<GitProcessResult> RunAsync(
        string subcommand,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? globalOptions = null,
        TimeSpan? timeout = null)
    {
        using var stdout = new MemoryStream();

        var outcome = await RunStreamingAsync(
            subcommand,
            arguments,
            workingDirectory,
            async (stream, token) => await stream.CopyToAsync(stdout, token).ConfigureAwait(false),
            cancellationToken,
            globalOptions,
            timeout).ConfigureAwait(false);

        return new GitProcessResult(
            outcome.ExitCode,
            Encoding.UTF8.GetString(stdout.GetBuffer(), 0, (int)stdout.Length),
            outcome.StandardError);
    }

    /// <summary>
    /// Runs git and hands <paramref name="readStandardOutput"/> the raw stdout stream.
    /// <para>
    /// Raw bytes on purpose: <c>-z</c> output is NUL-delimited and blob content is arbitrary, so
    /// running either through a UTF-8 <see cref="StreamReader"/> would corrupt it. stderr is
    /// drained concurrently — leaving a redirected pipe unread deadlocks the child as soon as it
    /// fills.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">The subcommand is not on the allowlist.</exception>
    public async Task<GitStreamOutcome> RunStreamingAsync(
        string subcommand,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Func<Stream, CancellationToken, Task> readStandardOutput,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? globalOptions = null,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subcommand);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(readStandardOutput);

        if (!PermittedSubcommands.Contains(subcommand))
        {
            throw new ArgumentException(
                $"'{subcommand}' is not on the read-only git allowlist. DiffHacker never mutates a repository.",
                nameof(subcommand));
        }

        var startInfo = CreateStartInfo(subcommand, arguments, workingDirectory, globalOptions);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout ?? DefaultTimeout);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("git did not start.");

            // Close the child's stdin immediately. Git is never given input, and the pipe exists
            // only so that it is not handed ours — see RedirectStandardInput in CreateStartInfo.
            process.StandardInput.Close();

            var stderr = ReadStandardErrorAsync(process, limit.Token);

            var stdout = process.StandardOutput.BaseStream;
            await readStandardOutput(stdout, limit.Token).ConfigureAwait(false);

            // A caller that stops early — a capped read, say — leaves git blocked writing into a
            // full pipe, and WaitForExitAsync would then sit there until the timeout. Drain the
            // rest and throw it away.
            await stdout.CopyToAsync(Stream.Null, limit.Token).ConfigureAwait(false);

            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);

            return new GitStreamOutcome(process.ExitCode, await stderr.ConfigureAwait(false));
        }
        catch (Win32Exception ex)
        {
            // The usual shape of "git is not installed" or "git is not on PATH".
            GitNotExecutable(logger, ex.Message);
            return GitStreamOutcome.NotExecutable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            GitTimedOut(logger, subcommand);
            KillQuietly(process);
            return GitStreamOutcome.TimedOut;
        }
        catch (OperationCanceledException)
        {
            // The caller gave up. Do not leave git running behind them.
            KillQuietly(process);
            throw;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private ProcessStartInfo CreateStartInfo(
        string subcommand,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyList<string>? globalOptions)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,

            // Redirected so the child cannot inherit ours, then closed the moment it starts.
            //
            // Without this, git is handed whatever stdin this process has. In the desktop
            // application that is harmless; in the standalone MCP server, stdin is the live
            // protocol pipe, and a git process holding it hung for the full timeout on every
            // single call — and could in principle have consumed protocol bytes. A read-only
            // subprocess has no business holding its parent's input channel either way.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Only stderr is read through a StreamReader; stdout is consumed as raw bytes so
            // that NUL-delimited records and binary blobs survive intact.
            StandardErrorEncoding = Encoding.UTF8,
        };

        // --no-pager before the subcommand: a pager attached to a redirected stream hangs.
        startInfo.ArgumentList.Add("--no-pager");

        // Locks are for writers. This application never writes, and a read that takes the index
        // lock can fail against a repository another tool is using.
        startInfo.ArgumentList.Add("--no-optional-locks");

        if (globalOptions is not null)
        {
            foreach (var option in globalOptions)
            {
                startInfo.ArgumentList.Add(option);
            }
        }

        startInfo.ArgumentList.Add(subcommand);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        // Never let git block on a credential or passphrase prompt: there is no terminal
        // attached, so it would hang until the timeout rather than failing.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        return startInfo;
    }

    private static async Task<string> ReadStandardErrorAsync(Process process, CancellationToken cancellationToken)
    {
        var buffer = new char[MaxStandardErrorBytes];
        var written = 0;

        while (written < buffer.Length)
        {
            var read = await process.StandardError
                .ReadAsync(buffer.AsMemory(written), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            written += read;
        }

        return new string(buffer, 0, written);
    }

    private static void KillQuietly(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // It already exited, or the OS will not let us signal it. Either way there is
            // nothing useful left to do.
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "git could not be executed: {Reason}")]
    private static partial void GitNotExecutable(ILogger logger, string reason);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "git {Subcommand} did not finish within the timeout and was killed.")]
    private static partial void GitTimedOut(ILogger logger, string subcommand);
}

/// <summary>Exit code and stderr from one streaming git invocation.</summary>
/// <param name="ExitCode">
/// Process exit code, <see cref="GitProcessResult.NotRunExitCode"/> when git could not be
/// executed, or <see cref="GitProcessResult.TimedOutExitCode"/> when it hung and was killed.
/// </param>
/// <param name="StandardError">Captured stderr, truncated to a bounded length.</param>
public readonly record struct GitStreamOutcome(int ExitCode, string StandardError)
{
    public static GitStreamOutcome NotExecutable => new(GitProcessResult.NotRunExitCode, string.Empty);

    public static GitStreamOutcome TimedOut => new(GitProcessResult.TimedOutExitCode, string.Empty);

    public bool Succeeded => ExitCode == 0;

    public bool CouldNotRun => ExitCode is GitProcessResult.NotRunExitCode or GitProcessResult.TimedOutExitCode;

    public bool TimedOutWaiting => ExitCode == GitProcessResult.TimedOutExitCode;
}

/// <summary>Streams and exit code from one git invocation.</summary>
/// <param name="ExitCode">Process exit code, or a negative sentinel when git did not run.</param>
/// <param name="StandardOutput">Captured stdout.</param>
/// <param name="StandardError">Captured stderr.</param>
public readonly record struct GitProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Git could not be started: not installed, or not on PATH.</summary>
    public const int NotRunExitCode = -1;

    /// <summary>
    /// Git started but did not finish in time and was killed. Distinct from
    /// <see cref="NotRunExitCode"/> because "git is missing" and "this repository is pathological"
    /// are different problems and the user deserves to be told which one they have.
    /// </summary>
    public const int TimedOutExitCode = -2;

    public static GitProcessResult NotExecutable => new(NotRunExitCode, string.Empty, string.Empty);

    public bool Succeeded => ExitCode == 0;

    public bool CouldNotRun => ExitCode is NotRunExitCode or TimedOutExitCode;

    public bool TimedOutWaiting => ExitCode == TimedOutExitCode;
}
