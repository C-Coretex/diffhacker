using DiffHacker.Host.Rpc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Host.Tests;

/// <summary>
/// "Delete all local data": the database, its WAL/SHM sidecars, the secret files and the diff
/// cache disappear immediately. The log directory is included too, and here — where nothing is
/// actually holding a log file open the way Serilog does in the real process — every file in it
/// disappears as well; <see cref="PendingDataWipeTests"/> covers the file this one leaves behind
/// on purpose, to prove the marker matters when something is still locked.
/// </summary>
public sealed class DataRpcTargetTests : IAsyncLifetime
{
    private readonly DirectoryInfo _dataDirectory =
        Directory.CreateTempSubdirectory("diffhacker-data-rpc-");

    private AppPaths _paths = null!;
    private FakeAppShell _shell = null!;
    private DataRpcTarget _target = null!;

    public ValueTask InitializeAsync()
    {
        _paths = new AppPaths(_dataDirectory.FullName);
        _paths.EnsureCreated();
        Directory.CreateDirectory(_paths.DiffCacheDirectory);

        File.WriteAllText(_paths.DatabaseFile, "db");
        File.WriteAllText(_paths.DatabaseFile + "-wal", "wal");
        File.WriteAllText(_paths.DatabaseFile + "-shm", "shm");
        File.WriteAllText(_paths.SecretsFile, "secrets");
        File.WriteAllText(_paths.MasterKeyFile, "key");
        File.WriteAllText(_paths.SecretSaltFile, "salt");

        Directory.CreateDirectory(Path.Combine(_paths.DiffCacheDirectory, "abc123"));
        File.WriteAllText(Path.Combine(_paths.DiffCacheDirectory, "abc123", "Contract.cs"), "cached");

        File.WriteAllText(Path.Combine(_paths.LogDirectory, "log20260101.txt"), "an older, rolled-over log");
        File.WriteAllText(_paths.LogFile, "today's log");

        _shell = new FakeAppShell();
        _target = new DataRpcTarget(_paths, _shell, NullLogger<DataRpcTarget>.Instance);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            _dataDirectory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A file handle outliving the test is not a test failure.
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Deletes_the_database_its_sidecars_the_secrets_and_the_diff_cache()
    {
        await _target.DeleteAllAsync(TestContext.Current.CancellationToken);

        File.Exists(_paths.DatabaseFile).ShouldBeFalse();
        File.Exists(_paths.DatabaseFile + "-wal").ShouldBeFalse();
        File.Exists(_paths.DatabaseFile + "-shm").ShouldBeFalse();

        File.Exists(_paths.SecretsFile).ShouldBeFalse();
        File.Exists(_paths.MasterKeyFile).ShouldBeFalse();
        File.Exists(_paths.SecretSaltFile).ShouldBeFalse();

        Directory.EnumerateFiles(_paths.DiffCacheDirectory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deletes_every_log_file_nothing_here_is_holding_open()
    {
        await _target.DeleteAllAsync(TestContext.Current.CancellationToken);

        Directory.EnumerateFiles(_paths.LogDirectory).ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaves_the_pending_wipe_marker_for_the_next_launch_to_check()
    {
        // Written unconditionally, whether or not this run actually left a log file behind:
        // DataRpcTarget cannot tell the difference between "removed" and "never had one to
        // remove", and either way the next launch is the only safe place to remove log.txt itself.
        await _target.DeleteAllAsync(TestContext.Current.CancellationToken);

        File.Exists(_paths.PendingLogWipeMarkerFile).ShouldBeTrue();
    }

    [Fact]
    public async Task Closes_the_window_shortly_after_rather_than_running_on_against_a_wiped_database()
    {
        await _target.DeleteAllAsync(TestContext.Current.CancellationToken);

        await WaitForCloseAsync();

        _shell.CloseCalls.ShouldBe(1);
    }

    private async Task WaitForCloseAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (_shell.CloseCalls == 0 && !timeout.IsCancellationRequested)
        {
            await Task.Delay(25, CancellationToken.None);
        }
    }
}
