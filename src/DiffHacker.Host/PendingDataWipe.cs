namespace DiffHacker.Host;

/// <summary>
/// Finishes a "delete all local data" request left over from the previous run.
/// <para>
/// <see cref="Rpc.DataRpcTarget"/> can erase everything under <see cref="AppPaths.DataDirectory"/>
/// except the log file it is actively writing to — Serilog holds that handle exclusively for the
/// life of the process — so it leaves <see cref="AppPaths.PendingLogWipeMarkerFile"/> instead. This
/// runs once, before <see cref="Logging.LoggingSetup"/> opens a new log file, so the directory ends
/// up genuinely empty rather than "empty except for one file nobody meant to keep".
/// </para>
/// </summary>
internal static class PendingDataWipe
{
    public static void ApplyIfRequested(AppPaths paths)
    {
        if (!File.Exists(paths.PendingLogWipeMarkerFile))
        {
            return;
        }

        if (Directory.Exists(paths.LogDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(paths.LogDirectory))
            {
                TryDelete(file);
            }
        }

        TryDelete(paths.PendingLogWipeMarkerFile);
    }

    // Best-effort: there is no logger yet at this point in start-up, and a file that resists
    // deletion here is no worse off than it was before the previous run asked to remove it.
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
