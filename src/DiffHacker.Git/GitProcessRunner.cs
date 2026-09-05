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
/// Iteration 3 builds <c>IGitClient</c> on this same runner and widens
/// <see cref="PermittedSubcommands"/> as it needs to.
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
    /// </summary>
    public static readonly IReadOnlySet<string> PermittedSubcommands =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "version",
            "rev-parse",
        };

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs <c>git &lt;subcommand&gt; &lt;arguments&gt;</c> and captures its streams.
    /// </summary>
    /// <exception cref="ArgumentException">The subcommand is not on the allowlist.</exception>
    public async Task<GitProcessResult> RunAsync(
        string subcommand,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subcommand);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!PermittedSubcommands.Contains(subcommand))
        {
            throw new ArgumentException(
                $"'{subcommand}' is not on the read-only git allowlist. DiffHacker never mutates a repository.",
                nameof(subcommand));
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // --no-pager before the subcommand: a pager attached to a redirected stream hangs.
        startInfo.ArgumentList.Add("--no-pager");
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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultTimeout);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("git did not start.");

            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            return new GitProcessResult(
                process.ExitCode,
                await stdout.ConfigureAwait(false),
                await stderr.ConfigureAwait(false));
        }
        catch (Win32Exception ex)
        {
            // The usual shape of "git is not installed" or "git is not on PATH".
            GitNotExecutable(logger, ex.Message);
            return GitProcessResult.NotExecutable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            GitTimedOut(logger, subcommand);
            return GitProcessResult.NotExecutable;
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "git could not be executed: {Reason}")]
    private static partial void GitNotExecutable(ILogger logger, string reason);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "git {Subcommand} did not finish within the timeout.")]
    private static partial void GitTimedOut(ILogger logger, string subcommand);
}

/// <summary>Streams and exit code from one git invocation.</summary>
/// <param name="ExitCode">Process exit code, or -1 when git could not be executed at all.</param>
/// <param name="StandardOutput">Captured stdout.</param>
/// <param name="StandardError">Captured stderr.</param>
public readonly record struct GitProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Git could not be started: not installed, not on PATH, or it hung.</summary>
    public static GitProcessResult NotExecutable => new(-1, string.Empty, string.Empty);

    public bool Succeeded => ExitCode == 0;

    public bool CouldNotRun => ExitCode == -1;
}
