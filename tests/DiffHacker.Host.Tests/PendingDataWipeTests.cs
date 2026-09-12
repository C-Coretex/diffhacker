namespace DiffHacker.Host.Tests;

/// <summary>
/// The half of "delete all local data" that <see cref="Rpc.DataRpcTargetTests"/> cannot exercise:
/// what happens to a log file that really was locked when the wipe ran, and is only found again
/// at the next launch.
/// </summary>
public sealed class PendingDataWipeTests
{
    [Fact]
    public void Does_nothing_when_no_wipe_was_requested()
    {
        var root = TempRoot();
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        File.WriteAllText(paths.LogFile, "an ordinary log file from an ordinary run");

        try
        {
            PendingDataWipe.ApplyIfRequested(paths);

            File.Exists(paths.LogFile).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Removes_every_leftover_log_file_and_the_marker_itself()
    {
        var root = TempRoot();
        var paths = new AppPaths(root);
        paths.EnsureCreated();

        // Stands in for the previous run's log.txt, which DataRpcTarget could not remove because
        // Serilog still held it open at the moment the wipe ran.
        File.WriteAllText(paths.LogFile, "last run's active log");
        File.WriteAllText(paths.PendingLogWipeMarkerFile, string.Empty);

        try
        {
            PendingDataWipe.ApplyIfRequested(paths);

            File.Exists(paths.LogFile).ShouldBeFalse();
            File.Exists(paths.PendingLogWipeMarkerFile).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "diffhacker-pending-wipe-" + Guid.NewGuid().ToString("n"));
}
