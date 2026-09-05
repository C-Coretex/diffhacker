using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using DiffHacker.Core.Secrets;

namespace DiffHacker.Storage.Secrets;

/// <summary>
/// macOS. Stores the master key as a generic password item in the login Keychain.
/// <para>
/// P/Invoked into Security.framework rather than shelling out to <c>/usr/bin/security</c>:
/// spawning that binary can raise its own authorisation dialogs, and a process that hangs
/// waiting for one would be indistinguishable from a broken keychain.
/// </para>
/// <para>
/// <b>Unverified on real hardware.</b> Nothing in this repository has ever been run on macOS —
/// CLAUDE.md records that CI is deferred and the platform is unproven. Failures here fall back
/// to <see cref="MachineDerivedMasterKeyProtector"/>, which is reported honestly in the
/// interface.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class KeychainMasterKeyProtector : IMasterKeyProtector
{
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";

    private const string ServiceName = "DiffHacker";
    private const string AccountName = "secret-store-master-key";

    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const int KeyLength = 32;

    public SecretBackendKind Backend => SecretBackendKind.MacosKeychain;

    public byte[] GetOrCreateMasterKey()
    {
        var existing = Find();
        if (existing is not null)
        {
            return existing;
        }

        var key = RandomNumberGenerator.GetBytes(KeyLength);
        Add(key);
        return key;
    }

    private static byte[]? Find()
    {
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(AccountName);

        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)service.Length,
            service,
            (uint)account.Length,
            account,
            out var length,
            out var data,
            IntPtr.Zero);

        if (status == ErrSecItemNotFound)
        {
            return null;
        }

        if (status != ErrSecSuccess)
        {
            throw new SecretStoreException($"The macOS Keychain returned status {status} while reading the master key.");
        }

        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return bytes;
        }
        finally
        {
            if (data != IntPtr.Zero)
            {
                _ = SecKeychainItemFreeContent(IntPtr.Zero, data);
            }
        }
    }

    private static void Add(byte[] key)
    {
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(AccountName);

        var status = SecKeychainAddGenericPassword(
            IntPtr.Zero,
            (uint)service.Length,
            service,
            (uint)account.Length,
            account,
            (uint)key.Length,
            key,
            IntPtr.Zero);

        if (status != ErrSecSuccess)
        {
            throw new SecretStoreException($"The macOS Keychain returned status {status} while storing the master key.");
        }
    }

    [LibraryImport(SecurityFramework)]
    private static partial int SecKeychainFindGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        [In] byte[] serviceName,
        uint accountNameLength,
        [In] byte[] accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        IntPtr itemRef);

    [LibraryImport(SecurityFramework)]
    private static partial int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        [In] byte[] serviceName,
        uint accountNameLength,
        [In] byte[] accountName,
        uint passwordLength,
        [In] byte[] passwordData,
        IntPtr itemRef);

    [LibraryImport(SecurityFramework)]
    private static partial int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);
}
