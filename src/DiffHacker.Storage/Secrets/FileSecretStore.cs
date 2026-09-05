using System.Security.Cryptography;
using System.Text.Json;
using DiffHacker.Core.Secrets;

namespace DiffHacker.Storage.Secrets;

/// <summary>
/// Every API key the application holds, in one AES-GCM encrypted file.
/// <para>
/// The encryption key comes from <see cref="IMasterKeyProtector"/>, which is the only part
/// that differs per operating system. Keys never reach SQLite, never reach <c>log.txt</c>, and
/// never travel back across the bridge into the WebView (CLAUDE.md §0.2.13).
/// </para>
/// </summary>
public sealed class FileSecretStore : ISecretStore, IDisposable
{
    /// <summary>Format marker, so a future change can be detected rather than misparsed.</summary>
    private const byte FormatVersion = 1;

    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly string _path;
    private readonly IMasterKeyProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal FileSecretStore(string path, IMasterKeyProtector protector, bool isFallback)
    {
        _path = path;
        _protector = protector;
        IsFallback = isFallback;
    }

    public SecretBackendKind Backend => _protector.Backend;

    public bool IsFallback { get; }

    public async ValueTask<string?> GetAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var secrets = Read();
            return secrets.TryGetValue(name, out var value) ? value : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> ContainsAsync(string name, CancellationToken cancellationToken) =>
        await GetAsync(name, cancellationToken).ConfigureAwait(false) is not null;

    public async ValueTask SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var secrets = Read();
            secrets[name] = value;
            Write(secrets);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DeleteAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var secrets = Read();
            if (secrets.Remove(name))
            {
                Write(secrets);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, string> Read()
    {
        byte[] encrypted;
        try
        {
            if (!File.Exists(_path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            encrypted = File.ReadAllBytes(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException($"The secret store at {_path} could not be read.", ex);
        }

        if (encrypted.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        if (encrypted.Length < 1 + NonceLength + TagLength || encrypted[0] != FormatVersion)
        {
            throw new SecretStoreException(
                $"The secret store at {_path} is not in a format this build understands.");
        }

        var key = _protector.GetOrCreateMasterKey();
        try
        {
            var nonce = encrypted.AsSpan(1, NonceLength);
            var tag = encrypted.AsSpan(1 + NonceLength, TagLength);
            var ciphertext = encrypted.AsSpan(1 + NonceLength + TagLength);
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext, StorageJson.Options)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (CryptographicException ex)
        {
            // Authentication failure: the file was tampered with, or the master key changed
            // (a restored backup on a different machine, say). Either way it cannot be read,
            // and pretending it is empty would silently discard the user's keys.
            throw new SecretStoreException(
                $"The secret store at {_path} could not be decrypted. It may have been copied from another machine.",
                ex);
        }
        catch (JsonException ex)
        {
            throw new SecretStoreException($"The secret store at {_path} decrypted to unreadable content.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private void Write(Dictionary<string, string> secrets)
    {
        var key = _protector.GetOrCreateMasterKey();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(secrets, StorageJson.Options);

        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var tag = new byte[TagLength];
            var ciphertext = new byte[plaintext.Length];

            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            var payload = new byte[1 + NonceLength + TagLength + ciphertext.Length];
            payload[0] = FormatVersion;
            nonce.CopyTo(payload.AsSpan(1));
            tag.CopyTo(payload.AsSpan(1 + NonceLength));
            ciphertext.CopyTo(payload.AsSpan(1 + NonceLength + TagLength));

            SecretFilePermissions.WriteRestricted(_path, payload);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException($"The secret store at {_path} could not be written.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Dispose() => _gate.Dispose();
}
