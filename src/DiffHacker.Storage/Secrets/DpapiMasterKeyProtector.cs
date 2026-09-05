using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using DiffHacker.Core.Secrets;

namespace DiffHacker.Storage.Secrets;

/// <summary>
/// Windows. Wraps the master key with DPAPI under the current user account and keeps the
/// wrapped blob in a file.
/// <para>
/// P/Invoked directly rather than through <c>System.Security.Cryptography.ProtectedData</c>,
/// to keep the dependency list at what CLAUDE.md §0.3 already names.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class DpapiMasterKeyProtector(string blobFile) : IMasterKeyProtector
{
    private const int KeyLength = 32;
    private const uint CryptProtectUiForbidden = 0x1;

    public SecretBackendKind Backend => SecretBackendKind.WindowsDpapi;

    public byte[] GetOrCreateMasterKey()
    {
        try
        {
            if (File.Exists(blobFile))
            {
                var blob = File.ReadAllBytes(blobFile);
                if (blob.Length > 0)
                {
                    return Unprotect(blob);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException($"The protected master key at {blobFile} could not be read.", ex);
        }

        var key = RandomNumberGenerator.GetBytes(KeyLength);
        SecretFilePermissions.WriteRestricted(blobFile, Protect(key));
        return key;
    }

    private static byte[] Protect(byte[] plaintext)
    {
        var input = default(DataBlob);
        var output = default(DataBlob);
        var handle = GCHandle.Alloc(plaintext, GCHandleType.Pinned);

        try
        {
            input.Data = handle.AddrOfPinnedObject();
            input.Size = plaintext.Length;

            if (!CryptProtectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output))
            {
                throw new SecretStoreException(
                    "Windows DPAPI refused to protect the master key.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            return Copy(output);
        }
        finally
        {
            handle.Free();
            Release(ref output);
        }
    }

    private static byte[] Unprotect(byte[] blob)
    {
        var input = default(DataBlob);
        var output = default(DataBlob);
        var handle = GCHandle.Alloc(blob, GCHandleType.Pinned);

        try
        {
            input.Data = handle.AddrOfPinnedObject();
            input.Size = blob.Length;

            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output))
            {
                throw new SecretStoreException(
                    "Windows DPAPI refused to unprotect the master key. It was most likely written by a different user account.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            return Copy(output);
        }
        finally
        {
            handle.Free();
            Release(ref output);
        }
    }

    private static byte[] Copy(DataBlob blob)
    {
        var bytes = new byte[blob.Size];
        Marshal.Copy(blob.Data, bytes, 0, blob.Size);
        return bytes;
    }

    private static void Release(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
        {
            return;
        }

        // The plaintext key passed through this buffer, so clear it before handing it back.
        for (var offset = 0; offset < blob.Size; offset++)
        {
            Marshal.WriteByte(blob.Data, offset, 0);
        }

        _ = LocalFree(blob.Data);
        blob.Data = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        ref DataBlob output);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        ref DataBlob output);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr LocalFree(IntPtr handle);
}
