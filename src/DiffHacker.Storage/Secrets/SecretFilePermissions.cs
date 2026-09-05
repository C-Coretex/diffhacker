namespace DiffHacker.Storage.Secrets;

/// <summary>
/// Writes a file that only the owning user can read.
/// <para>
/// On Unix that means mode 0600, set before any bytes are written — creating the file
/// world-readable and tightening it afterwards leaves a window. On Windows the file inherits
/// the ACL of the per-user application data directory, which is already restricted to the
/// user, and .NET exposes no portable ACL API to narrow it further.
/// </para>
/// </summary>
internal static class SecretFilePermissions
{
    public static void WriteRestricted(string path, byte[] contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(path, contents);
            return;
        }

        // Create with the restrictive mode in place, then write, so the contents are never
        // momentarily readable by other users.
        using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });

        stream.Write(contents);
    }
}
