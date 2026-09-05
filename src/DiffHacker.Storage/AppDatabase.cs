using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Storage;

/// <summary>
/// The SQLite database in the per-user application data directory, and its migrations.
/// <para>
/// Settings never live inside the user's repository. The application is read-only with respect
/// to the repositories it reviews (§0.2.12), and writing state into one would break that in the
/// most surprising possible way.
/// </para>
/// <para>
/// Initialisation is lazy rather than a startup step: it keeps <c>Program.Run</c> free of
/// sync-over-async, and turns a corrupt database into a typed error on the first call that
/// needs it instead of a crash before the window opens.
/// </para>
/// </summary>
public sealed partial class AppDatabase : IAsyncDisposable
{
    /// <summary>
    /// Bumped whenever <see cref="MigrateAsync"/> gains a step. Stored in the file, so an older
    /// build opening a newer database can say so rather than misreading it.
    /// </summary>
    private const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;
    private readonly ILogger<AppDatabase> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialised;

    public AppDatabase(string databaseFile, ILogger<AppDatabase> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        _logger = logger;

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Opens a connection, running migrations once per process.</summary>
    public async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (!_initialised)
        {
            await InitialiseAsync(cancellationToken).ConfigureAwait(false);
        }

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async ValueTask InitialiseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialised)
            {
                return;
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // WAL survives an abrupt exit far better than the default rollback journal, and
            // this file is written from a desktop app the user can close at any moment.
            await connection.ExecuteAsync(new CommandDefinition(
                "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;",
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await MigrateAsync(connection, cancellationToken).ConfigureAwait(false);

            _initialised = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);",
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var version = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT version FROM schema_version LIMIT 1;",
            cancellationToken: cancellationToken)).ConfigureAwait(false) ?? 0;

        if (version > CurrentSchemaVersion)
        {
            throw new StorageException(
                $"The settings database is at schema version {version}, but this build understands {CurrentSchemaVersion}. "
                + "It was written by a newer version of DiffHacker.");
        }

        if (version == CurrentSchemaVersion)
        {
            return;
        }

        MigratingDatabase(_logger, version, CurrentSchemaVersion);

        if (version < 1)
        {
            // No api_key column, here or anywhere else in this file. Keys belong to
            // ISecretStore alone (CLAUDE.md §0.2.13), and a test asserts none reaches SQLite.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                CREATE TABLE recent_repositories (
                    path            TEXT PRIMARY KEY,
                    name            TEXT NOT NULL,
                    last_opened_utc TEXT NOT NULL
                );

                CREATE TABLE provider_profiles (
                    id             TEXT PRIMARY KEY,
                    provider_type  TEXT NOT NULL,
                    display_name   TEXT NOT NULL,
                    model          TEXT NOT NULL,
                    base_url       TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    model_suggestions TEXT NULL
                );

                CREATE TABLE app_settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE INDEX ix_recent_repositories_last_opened
                    ON recent_repositories (last_opened_utc DESC);
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM schema_version;
            INSERT INTO schema_version (version) VALUES (@version);
            """,
            new { version = CurrentSchemaVersion },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        SqliteConnection.ClearAllPools();
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Migrating the settings database from schema {From} to {To}.")]
    private static partial void MigratingDatabase(ILogger logger, int from, int to);
}

/// <summary>Thrown when the settings database cannot be opened or is not understood.</summary>
public sealed class StorageException(string message, Exception? innerException = null)
    : Exception(message, innerException);
