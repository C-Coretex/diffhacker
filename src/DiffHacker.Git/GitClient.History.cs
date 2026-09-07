using DiffHacker.Core.Changes;

namespace DiffHacker.Git;

/// <summary>
/// The two questions a stored project profile asks of history: which commit it was taken from, and
/// how far the repository has moved since.
/// <para>
/// Both are answered with subcommands already on the allowlist. Counting commits would have been
/// the more obvious measure of drift and would have needed <c>rev-list</c> added to it — a change
/// to the product's read-only contract, for a worse signal. <c>diff --raw</c> answers in files,
/// which is what actually makes a profile stale.
/// </para>
/// </summary>
public sealed partial class GitClient
{
    public async Task<string?> GetHeadCommitAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var root = await RequireRepositoryAsync(repositoryPath, cancellationToken).ConfigureAwait(false);

        var head = await runner
            .RunAsync("rev-parse", ["--verify", "--quiet", "HEAD"], root, cancellationToken)
            .ConfigureAwait(false);

        if (head.CouldNotRun)
        {
            throw Unavailable(head);
        }

        // An unborn HEAD is a repository with no commits, which is an ordinary state — a first
        // commit's worth of work is still a repository worth profiling.
        return head.Succeeded && head.StandardOutput.Trim() is { Length: > 0 } sha ? sha : null;
    }

    public async Task<CommitComparison> CompareWithHeadAsync(
        string repositoryPath,
        string commitSha,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        if (!IsObjectName(commitSha))
        {
            // The value comes from our own database, but it reaches a command line either way, and
            // a stored string that begins with a dash would be read by git as an option. Refusing
            // anything that is not an object name is cheaper than reasoning about which options
            // would be harmful.
            return new CommitComparison(CommitReachable: false, FilesChanged: 0);
        }

        var root = await RequireRepositoryAsync(repositoryPath, cancellationToken).ConfigureAwait(false);

        var resolved = await runner
            .RunAsync("rev-parse", ["--verify", "--quiet", commitSha + "^{commit}"], root, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.CouldNotRun)
        {
            throw Unavailable(resolved);
        }

        if (!resolved.Succeeded)
        {
            // Rebased away, rewritten, or never fetched into a shallow clone. Not an error: "we
            // cannot tell how old this profile is" is itself a reason to offer a new one.
            return new CommitComparison(CommitReachable: false, FilesChanged: 0);
        }

        var head = await runner
            .RunAsync("rev-parse", ["--verify", "--quiet", "HEAD"], root, cancellationToken)
            .ConfigureAwait(false);

        if (head.CouldNotRun)
        {
            throw Unavailable(head);
        }

        if (!head.Succeeded)
        {
            return new CommitComparison(CommitReachable: false, FilesChanged: 0);
        }

        var changed = 0;

        var outcome = await RunDiffAsync(
            root,
            ["--raw", "-z", "--no-abbrev", .. CommonDiffOptions, commitSha, "HEAD", "--"],
            async (stream, token) =>
            {
                await foreach (var _ in GitOutputReaders.ReadRawAsync(stream, token).ConfigureAwait(false))
                {
                    changed++;
                }
            },
            cancellationToken).ConfigureAwait(false);

        RequireSuccess(outcome, "diff --raw between the profile's commit and HEAD");

        return new CommitComparison(CommitReachable: true, FilesChanged: changed);
    }

    /// <summary>
    /// Whether a string is a plain hexadecimal object name and nothing else — no refs, no
    /// revision syntax, no leading dash.
    /// </summary>
    private static bool IsObjectName(string value)
    {
        if (value.Length is < 4 or > 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
