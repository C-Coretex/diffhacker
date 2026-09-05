using System.Security.Cryptography;
using System.Text;
using DiffHacker.Core.Secrets;

namespace DiffHacker.Storage.Secrets;

/// <summary>
/// The fallback for systems with no keyring daemon — common on headless and minimal Linux
/// installs, and the reason Iteration 2 requires a fallback at all.
/// <para>
/// <b>The honest claim, which the interface repeats:</b> the key is derived from a random
/// per-install salt file plus machine and user identifiers. That protects the secrets file if
/// it is copied to another machine or read out of a backup. It does <i>not</i> protect against
/// anyone who can already run code as this user — they can simply derive the same key.
/// </para>
/// <para>
/// A user passphrase would be stronger, and was considered and rejected during planning: it
/// would mean a prompt on every launch, and a forgotten passphrase would mean re-entering
/// every API key.
/// </para>
/// </summary>
internal sealed class MachineDerivedMasterKeyProtector(string saltFile) : IMasterKeyProtector
{
    private const int SaltLength = 32;
    private const int KeyLength = 32;

    public SecretBackendKind Backend => SecretBackendKind.MachineDerived;

    public byte[] GetOrCreateMasterKey()
    {
        var salt = ReadOrCreateSalt();

        // HKDF, not a password KDF: the input is high-entropy machine material rather than a
        // human-chosen secret, so there is nothing for an expensive KDF to defend against.
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: Encoding.UTF8.GetBytes(MachineMaterial()),
            outputLength: KeyLength,
            salt: salt,
            info: "DiffHacker.SecretStore.v1"u8.ToArray());
    }

    private byte[] ReadOrCreateSalt()
    {
        try
        {
            if (File.Exists(saltFile))
            {
                var existing = File.ReadAllBytes(saltFile);
                if (existing.Length == SaltLength)
                {
                    return existing;
                }
            }

            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            SecretFilePermissions.WriteRestricted(saltFile, salt);
            return salt;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException($"The secret salt file at {saltFile} could not be read or created.", ex);
        }
    }

    /// <summary>
    /// Machine and user identifiers. Individually weak, and not treated as secret — the salt
    /// is what makes the derived key unpredictable off this machine.
    /// </summary>
    private static string MachineMaterial() =>
        string.Join(
            '|',
            Environment.MachineName,
            Environment.UserName,
            Environment.OSVersion.Platform.ToString(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}
