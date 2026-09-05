using DiffHacker.Core.Providers;

namespace DiffHacker.Core.Settings;

/// <summary>
/// Persistence for configured LLM providers. Stores everything about a provider except its
/// API key, which belongs to <c>ISecretStore</c> alone.
/// </summary>
public interface IProviderProfileStore
{
    /// <summary>Ordered by display name.</summary>
    ValueTask<IReadOnlyList<LlmProviderProfile>> ListAsync(CancellationToken cancellationToken);

    ValueTask<LlmProviderProfile?> FindAsync(string id, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces the profile under its own identifier.</summary>
    ValueTask SaveAsync(LlmProviderProfile profile, CancellationToken cancellationToken);

    ValueTask DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>Identifier of the profile analysis runs will use, or null when none is set.</summary>
    ValueTask<string?> GetActiveIdAsync(CancellationToken cancellationToken);

    ValueTask SetActiveIdAsync(string? id, CancellationToken cancellationToken);
}
