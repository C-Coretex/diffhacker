using System.Security.Cryptography;
using System.Text;
using DiffHacker.Core.Secrets;
using DiffHacker.Storage.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Storage.Tests;

/// <summary>
/// The secret store is the one component whose failure mode is a leaked API key, so the tests
/// here are adversarial rather than confirmatory.
/// </summary>
public sealed class SecretStoreTests
{
    private const string Key = "sk-test-abcdefghijklmnopqrstuvwxyz0123456789";

    [Fact]
    public async Task A_secret_round_trips()
    {
        using var directory = new TemporaryDataDirectory();
        using var store = CreateStore(directory);

        await store.SetAsync("provider:abc", Key, TestContext.Current.CancellationToken);

        (await store.GetAsync("provider:abc", TestContext.Current.CancellationToken)).ShouldBe(Key);
        (await store.ContainsAsync("provider:abc", TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_secret_survives_reopening_the_store()
    {
        using var directory = new TemporaryDataDirectory();

        using (var first = CreateStore(directory))
        {
            await first.SetAsync("provider:abc", Key, TestContext.Current.CancellationToken);
        }

        using var second = CreateStore(directory);
        (await second.GetAsync("provider:abc", TestContext.Current.CancellationToken)).ShouldBe(Key);
    }

    [Fact]
    public async Task A_deleted_secret_is_gone()
    {
        using var directory = new TemporaryDataDirectory();
        using var store = CreateStore(directory);

        await store.SetAsync("provider:abc", Key, TestContext.Current.CancellationToken);
        await store.DeleteAsync("provider:abc", TestContext.Current.CancellationToken);

        (await store.GetAsync("provider:abc", TestContext.Current.CancellationToken)).ShouldBeNull();
        (await store.ContainsAsync("provider:abc", TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_secret_is_null_rather_than_an_error()
    {
        using var directory = new TemporaryDataDirectory();
        using var store = CreateStore(directory);

        (await store.GetAsync("provider:never-stored", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task The_key_never_appears_in_the_file_on_disk()
    {
        using var directory = new TemporaryDataDirectory();
        using var store = CreateStore(directory);

        await store.SetAsync("provider:abc", Key, TestContext.Current.CancellationToken);

        var raw = await File.ReadAllBytesAsync(directory.SecretsFile, TestContext.Current.CancellationToken);

        Encoding.UTF8.GetString(raw).ShouldNotContain(Key);
        Contains(raw, Encoding.UTF8.GetBytes(Key)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_tampered_file_is_rejected_rather_than_silently_emptied()
    {
        using var directory = new TemporaryDataDirectory();

        using (var store = CreateStore(directory))
        {
            await store.SetAsync("provider:abc", Key, TestContext.Current.CancellationToken);
        }

        // Flip one bit of ciphertext. AES-GCM authenticates, so this must fail loudly:
        // returning an empty store would silently discard every key the user configured.
        var bytes = await File.ReadAllBytesAsync(directory.SecretsFile, TestContext.Current.CancellationToken);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(directory.SecretsFile, bytes, TestContext.Current.CancellationToken);

        using var reopened = CreateStore(directory);

        await Should.ThrowAsync<SecretStoreException>(
            async () => await reopened.GetAsync("provider:abc", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_machine_derived_fallback_round_trips_and_is_stable_across_instances()
    {
        using var directory = new TemporaryDataDirectory();

        var first = new MachineDerivedMasterKeyProtector(directory.SaltFile);
        using (var store = new FileSecretStore(directory.SecretsFile, first, isFallback: true))
        {
            store.Backend.ShouldBe(SecretBackendKind.MachineDerived);
            store.IsFallback.ShouldBeTrue();
            await store.SetAsync("provider:abc", Key, TestContext.Current.CancellationToken);
        }

        // A fresh protector must derive the same key from the persisted salt, or every restart
        // would lose the user's credentials.
        var second = new MachineDerivedMasterKeyProtector(directory.SaltFile);
        using var reopened = new FileSecretStore(directory.SecretsFile, second, isFallback: true);

        (await reopened.GetAsync("provider:abc", TestContext.Current.CancellationToken)).ShouldBe(Key);
    }

    [Fact]
    public void A_different_salt_derives_a_different_key()
    {
        using var directory = new TemporaryDataDirectory();

        var mine = new MachineDerivedMasterKeyProtector(directory.SaltFile).GetOrCreateMasterKey();
        var theirs = new MachineDerivedMasterKeyProtector(Path.Combine(directory.Root, "other.salt"))
            .GetOrCreateMasterKey();

        mine.ShouldNotBe(theirs, "The salt is what makes a copied secrets file useless elsewhere.");
        mine.Length.ShouldBe(32);
    }

    [Fact]
    public void The_factory_falls_back_when_the_platform_backend_throws()
    {
        using var directory = new TemporaryDataDirectory();

        // Standing in for a Linux box with libsecret present but no keyring daemon listening —
        // the case Iteration 2 requires a fallback for, and the one that cannot be reproduced
        // on this machine.
        using var store = SecretStoreFactory.Create(
            directory.SecretsFile,
            directory.MasterKeyFile,
            directory.SaltFile,
            NullLogger.Instance,
            _ => new ThrowingProtector());

        store.Backend.ShouldBe(SecretBackendKind.MachineDerived);
        store.IsFallback.ShouldBeTrue();
    }

    [Fact]
    public async Task The_store_still_works_after_falling_back()
    {
        using var directory = new TemporaryDataDirectory();

        using var store = SecretStoreFactory.Create(
            directory.SecretsFile,
            directory.MasterKeyFile,
            directory.SaltFile,
            NullLogger.Instance,
            _ => new ThrowingProtector());

        await store.SetAsync("provider:abc", Key, TestContext.Current.CancellationToken);
        (await store.GetAsync("provider:abc", TestContext.Current.CancellationToken)).ShouldBe(Key);
    }

    [Fact]
    public void The_platform_backend_is_used_when_it_works()
    {
        using var directory = new TemporaryDataDirectory();

        // On this machine that is DPAPI. On macOS and Linux the same path exercises Keychain
        // and libsecret, which is the only place those get any coverage at all.
        using var store = SecretStoreFactory.Create(
            directory.SecretsFile,
            directory.MasterKeyFile,
            directory.SaltFile,
            NullLogger.Instance,
            platformProtector: null);

        if (OperatingSystem.IsWindows())
        {
            store.Backend.ShouldBe(SecretBackendKind.WindowsDpapi);
            store.IsFallback.ShouldBeFalse();
        }
        else
        {
            store.Backend.ShouldBeOneOf(
                SecretBackendKind.MacosKeychain,
                SecretBackendKind.LinuxLibsecret,
                SecretBackendKind.MachineDerived);
        }
    }

    private static FileSecretStore CreateStore(TemporaryDataDirectory directory) =>
        new(directory.SecretsFile, new FixedKeyProtector(), isFallback: false);

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var start = 0; start + needle.Length <= haystack.Length; start++)
        {
            if (haystack.AsSpan(start, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A deterministic protector, so the file format can be tested without a keyring.</summary>
    private sealed class FixedKeyProtector : IMasterKeyProtector
    {
        public SecretBackendKind Backend => SecretBackendKind.MachineDerived;

        public byte[] GetOrCreateMasterKey() => SHA256.HashData("diffhacker-test-master-key"u8.ToArray());
    }

    /// <summary>Stands in for an unavailable platform credential store.</summary>
    private sealed class ThrowingProtector : IMasterKeyProtector
    {
        public SecretBackendKind Backend => SecretBackendKind.LinuxLibsecret;

        public byte[] GetOrCreateMasterKey() =>
            throw new SecretStoreException("No keyring daemon is listening.");
    }
}
