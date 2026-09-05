using Microsoft.Data.Sqlite;

namespace DiffHacker.Storage.Tests;

/// <summary>A throwaway application data directory, cleaned up with the test.</summary>
internal sealed class TemporaryDataDirectory : IDisposable
{
    public TemporaryDataDirectory() =>
        Root = Directory.CreateTempSubdirectory("diffhacker-storage-").FullName;

    public string Root { get; }

    public string DatabaseFile => Path.Combine(Root, "diffhacker.db");

    public string SecretsFile => Path.Combine(Root, "secrets.dat");

    public string MasterKeyFile => Path.Combine(Root, "masterkey.dat");

    public string SaltFile => Path.Combine(Root, "secrets.salt");

    public void Dispose()
    {
        try
        {
            // Pooled connections keep a handle on the file, which blocks the delete on Windows.
            SqliteConnection.ClearAllPools();
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
