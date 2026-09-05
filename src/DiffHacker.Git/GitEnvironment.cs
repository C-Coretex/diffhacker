using DiffHacker.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Git;

/// <summary>
/// Probes for the git command line once and caches the answer for the life of the process.
/// <para>
/// Re-probing on every call would mean spawning a process each time the interface asks whether
/// the application works, and git does not appear on <c>PATH</c> halfway through a session.
/// </para>
/// </summary>
public sealed partial class GitEnvironment : IGitEnvironment
{
    private readonly Lazy<Task<GitAvailability>> _probe;

    public GitEnvironment(GitProcessRunner runner, ILogger<GitEnvironment> logger)
    {
        // Lazy<Task<T>> rather than a semaphore: one probe regardless of how many callers race,
        // and no disposable state to own.
        _probe = new Lazy<Task<GitAvailability>>(() => ProbeOnceAsync(runner, logger));
    }

    public async ValueTask<GitAvailability> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _probe.Value.ConfigureAwait(false);
    }

    private static async Task<GitAvailability> ProbeOnceAsync(GitProcessRunner runner, ILogger logger)
    {
        var result = await runner.RunAsync("version", [], null, CancellationToken.None).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            GitMissing(logger);
            return GitAvailability.Missing;
        }

        var version = result.StandardOutput.Trim();
        GitFound(logger, version);
        return new GitAvailability(true, version);
    }

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information, Message = "Found git on PATH: {Version}")]
    private static partial void GitFound(ILogger logger, string version);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Error,
        Message = "No usable git was found on PATH. DiffHacker cannot review a repository without it.")]
    private static partial void GitMissing(ILogger logger);
}
