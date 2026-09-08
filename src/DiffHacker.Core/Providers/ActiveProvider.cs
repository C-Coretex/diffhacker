using DiffHacker.Core.Settings;

namespace DiffHacker.Core.Providers;

/// <summary>
/// Which provider profile a run should use.
/// <para>
/// Shared by every orchestrator that starts a conversation, because "which provider" is a question
/// with one right answer and two callers that disagreed about it would be a bug nobody could see
/// from either side.
/// </para>
/// </summary>
public static class ActiveProvider
{
    /// <summary>
    /// The provider to run against, or null when there is none to choose.
    /// <para>
    /// Falls back to the only configured profile when none is marked active: a user who added one
    /// provider and never pressed "use this" has told us which one they mean, and answering "you
    /// have no provider" would be pedantry rather than accuracy.
    /// </para>
    /// </summary>
    public static async ValueTask<LlmProviderProfile?> ResolveAsync(
        IProviderProfileStore providers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var activeId = await providers.GetActiveIdAsync(cancellationToken).ConfigureAwait(false);

        if (activeId is not null &&
            await providers.FindAsync(activeId, cancellationToken).ConfigureAwait(false) is { } active)
        {
            return active;
        }

        var all = await providers.ListAsync(cancellationToken).ConfigureAwait(false);
        return all.Count == 1 ? all[0] : null;
    }
}
