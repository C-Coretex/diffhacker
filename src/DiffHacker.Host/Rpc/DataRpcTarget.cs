using DiffHacker.Host.Shell;
using DiffHacker.Storage;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Erases everything DiffHacker has written to disk for this user: the settings database, every
/// stored API key, the cached diff extracts, and the log files — the whole of
/// <see cref="AppPaths.DataDirectory"/>.
/// <para>
/// Not the write exception §0.2.12 names: nothing here touches a repository DiffHacker reviews,
/// only the application's own state, in its own data directory.
/// </para>
/// <para>
/// Two things stop this from being a plain sweep. The database is left un-migrated once its file
/// is gone — a fresh, empty one appears the moment anything opens a connection — so this process
/// must not keep running against it; and the active log file cannot be removed while this
/// process still has it open, because Serilog holds it exclusively
/// (<see cref="Logging.LoggingSetup"/>). Both are why the window closes shortly after: closing is
/// what makes the log file's removal possible at all, and it happens on the next launch, before
/// logging starts (<see cref="PendingDataWipe"/>).
/// </para>
/// </summary>
public sealed partial class DataRpcTarget(AppPaths paths, IAppShell shell, ILogger<DataRpcTarget> logger)
{
    /// <summary>Gives the JSON-RPC response time to reach the renderer before the window closes.</summary>
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(500);

    [JsonRpcMethod("data.deleteAll")]
    public Task DeleteAllAsync(CancellationToken cancellationToken)
    {
        DeletingAllLocalData(logger);

        AppDatabase.ClearPools();
        DeleteFile(paths.DatabaseFile);
        DeleteFile(paths.DatabaseFile + "-wal");
        DeleteFile(paths.DatabaseFile + "-shm");

        DeleteFile(paths.SecretsFile);
        DeleteFile(paths.MasterKeyFile);
        DeleteFile(paths.SecretSaltFile);

        DeleteDirectoryContents(paths.DiffCacheDirectory);

        // Whatever is not locked by this process's own logger goes now; whatever is left (today's
        // active log.txt) is picked up at the next launch instead, via the marker below.
        DeleteDirectoryContents(paths.LogDirectory);
        MarkLogWipePending();

        AllLocalDataDeleted(logger);

        // Fire-and-forget rather than awaited: the caller gets its response now, and the window
        // closes a moment later on its own. There is nothing left worth keeping the process open
        // for — continuing to run against a wiped, un-migrated database is not a state to support.
        _ = CloseShellSoonAsync();

        return Task.CompletedTask;
    }

    private async Task CloseShellSoonAsync()
    {
        await Task.Delay(CloseDelay).ConfigureAwait(false);
        shell.Close();
    }

    private void MarkLogWipePending()
    {
        try
        {
            File.WriteAllText(paths.PendingLogWipeMarkerFile, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWipeMarkerFailed(logger, ex);
        }
    }

    private void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileDeletionFailed(logger, path, ex);
        }
    }

    private void DeleteDirectoryContents(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            DeleteFile(file);
        }
    }

    [LoggerMessage(EventId = 8001, Level = LogLevel.Warning, Message = "Deleting all local data, at the user's request.")]
    private static partial void DeletingAllLocalData(ILogger logger);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Information, Message = "All local data deleted; closing the window.")]
    private static partial void AllLocalDataDeleted(ILogger logger);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Warning, Message = "Could not delete {Path} while clearing local data.")]
    private static partial void FileDeletionFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 8004, Level = LogLevel.Warning, Message = "Could not record the pending log wipe.")]
    private static partial void LogWipeMarkerFailed(ILogger logger, Exception exception);
}
