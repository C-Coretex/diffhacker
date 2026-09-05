using DiffHacker.Core.Repositories;

namespace DiffHacker.Core.Settings;

/// <summary>
/// The recent-repository list. Lives in the per-user application data directory, never inside
/// the user's repository.
/// </summary>
public interface IRecentRepositoryStore
{
    /// <summary>Most recently opened first. <c>Available</c> is not populated here.</summary>
    ValueTask<IReadOnlyList<RecentRepository>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Records an open, moving an existing entry to the front.</summary>
    ValueTask RememberAsync(string path, string name, CancellationToken cancellationToken);

    /// <summary>Removes one entry. Nothing on disk is touched.</summary>
    ValueTask ForgetAsync(string path, CancellationToken cancellationToken);
}
