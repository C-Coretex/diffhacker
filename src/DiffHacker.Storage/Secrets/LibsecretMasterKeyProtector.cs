using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using DiffHacker.Core.Secrets;

namespace DiffHacker.Storage.Secrets;

/// <summary>
/// Linux. Stores the master key through libsecret, which fronts the Secret Service API that
/// GNOME Keyring and KWallet implement.
/// <para>
/// Bound directly to <c>libsecret-1.so.0</c> rather than shelling out to <c>secret-tool</c>:
/// that binary lives in the separate <c>libsecret-tools</c> package, which minimal and server
/// installs routinely omit. Depending on it would push machines onto the weaker fallback that
/// do not need to be there.
/// </para>
/// <para>
/// <b>Unverified on real hardware.</b> Nothing in this repository has ever been run on Linux.
/// When the library is missing or no keyring daemon is listening — the ordinary case on
/// headless installs, and the reason Iteration 2 requires a fallback — this throws and
/// <see cref="MachineDerivedMasterKeyProtector"/> takes over, which the interface reports.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LibsecretMasterKeyProtector : IMasterKeyProtector
{
    private const string Library = "libsecret-1.so.0";

    private const string Label = "DiffHacker secret store master key";
    private const string AttributeName = "application";
    private const string AttributeValue = "diffhacker";

    private const int KeyLength = 32;

    public SecretBackendKind Backend => SecretBackendKind.LinuxLibsecret;

    public byte[] GetOrCreateMasterKey()
    {
        var existing = Lookup();
        if (existing is not null)
        {
            return existing;
        }

        var key = RandomNumberGenerator.GetBytes(KeyLength);
        Store(Convert.ToBase64String(key));
        return key;
    }

    private static byte[]? Lookup()
    {
        var schema = CreateSchema();
        var error = IntPtr.Zero;

        try
        {
            var result = secret_password_lookup_sync(
                schema,
                IntPtr.Zero,
                ref error,
                AttributeName,
                AttributeValue,
                IntPtr.Zero);

            ThrowOnError(ref error, "reading");

            if (result == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var encoded = Marshal.PtrToStringUTF8(result);
                return string.IsNullOrEmpty(encoded) ? null : Convert.FromBase64String(encoded);
            }
            finally
            {
                secret_password_free(result);
            }
        }
        catch (FormatException ex)
        {
            throw new SecretStoreException("The master key stored in the keyring is not valid base64.", ex);
        }
        finally
        {
            secret_schema_unref(schema);
        }
    }

    private static void Store(string encodedKey)
    {
        var schema = CreateSchema();
        var error = IntPtr.Zero;

        try
        {
            var stored = secret_password_store_sync(
                schema,
                // The default collection is the user's login keyring.
                "default",
                Label,
                encodedKey,
                IntPtr.Zero,
                ref error,
                AttributeName,
                AttributeValue,
                IntPtr.Zero);

            ThrowOnError(ref error, "storing");

            if (!stored)
            {
                throw new SecretStoreException("libsecret declined to store the master key.");
            }
        }
        finally
        {
            secret_schema_unref(schema);
        }
    }

    private static IntPtr CreateSchema() =>
        secret_schema_new(
            "dev.diffhacker.SecretStore",
            SecretSchemaFlags.None,
            AttributeName,
            SecretSchemaAttributeType.String,
            IntPtr.Zero);

    private static void ThrowOnError(ref IntPtr error, string operation)
    {
        if (error == IntPtr.Zero)
        {
            return;
        }

        // GError layout is { GQuark domain; gint code; gchar* message; }.
        var messagePointer = Marshal.ReadIntPtr(error, IntPtr.Size);
        var message = Marshal.PtrToStringUTF8(messagePointer) ?? "no detail";
        g_error_free(error);
        error = IntPtr.Zero;

        throw new SecretStoreException($"libsecret failed while {operation} the master key: {message}");
    }

    private enum SecretSchemaFlags
    {
        None = 0,
    }

    private enum SecretSchemaAttributeType
    {
        String = 0,
    }

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr secret_schema_new(
        string name,
        SecretSchemaFlags flags,
        string attribute,
        SecretSchemaAttributeType attributeType,
        IntPtr sentinel);

    [LibraryImport(Library)]
    private static partial void secret_schema_unref(IntPtr schema);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr secret_password_lookup_sync(
        IntPtr schema,
        IntPtr cancellable,
        ref IntPtr error,
        string attribute,
        string attributeValue,
        IntPtr sentinel);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool secret_password_store_sync(
        IntPtr schema,
        string collection,
        string label,
        string password,
        IntPtr cancellable,
        ref IntPtr error,
        string attribute,
        string attributeValue,
        IntPtr sentinel);

    [LibraryImport(Library)]
    private static partial void secret_password_free(IntPtr password);

    [LibraryImport("libglib-2.0.so.0")]
    private static partial void g_error_free(IntPtr error);
}
