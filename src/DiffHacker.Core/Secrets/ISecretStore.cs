namespace DiffHacker.Core.Secrets;

/// <summary>
/// The only place an API key is ever written.
/// <para>
/// Keys never reach SQLite, never reach <c>log.txt</c>, and never travel back across the
/// JSON-RPC bridge into the WebView (CLAUDE.md §0.2.13). A key crosses the bridge exactly
/// once, inbound, when the user types it into the settings form.
/// </para>
/// </summary>
public interface ISecretStore
{
    /// <summary>Which facility is protecting the store, for honest reporting in the interface.</summary>
    SecretBackendKind Backend { get; }

    /// <summary>True when the platform's credential store was unavailable and the fallback engaged.</summary>
    bool IsFallback { get; }

    ValueTask<string?> GetAsync(string name, CancellationToken cancellationToken);

    ValueTask SetAsync(string name, string value, CancellationToken cancellationToken);

    ValueTask DeleteAsync(string name, CancellationToken cancellationToken);

    /// <summary>Whether a secret exists, without reading it into memory as a return value.</summary>
    ValueTask<bool> ContainsAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Thrown when the secret store cannot be opened at all.</summary>
public sealed class SecretStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
