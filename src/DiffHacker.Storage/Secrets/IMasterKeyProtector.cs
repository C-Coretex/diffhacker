using DiffHacker.Core.Secrets;

namespace DiffHacker.Storage.Secrets;

/// <summary>
/// Supplies the 32-byte key that <see cref="FileSecretStore"/> encrypts everything with, and
/// owns wherever that key is kept.
/// <para>
/// This is the whole of the per-OS surface. Rather than putting every API key into the
/// platform credential store — three native round trips per read, on two platforms that have
/// never been run — one key goes in, and the secrets themselves live in a single AES-GCM file.
/// Every platform then exercises the same file code path, including the fallback.
/// </para>
/// </summary>
internal interface IMasterKeyProtector
{
    SecretBackendKind Backend { get; }

    /// <summary>
    /// Returns the master key, creating and storing one on first use.
    /// </summary>
    /// <exception cref="SecretStoreException">The backend is unavailable or refused.</exception>
    byte[] GetOrCreateMasterKey();
}
