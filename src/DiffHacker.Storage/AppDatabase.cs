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
    private const int CurrentSchemaVersion = 6;

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

        if (version < 2)
        {
            // Iteration 4: a per-profile price override. The bundled price table is a snapshot
            // and goes stale, so a user on a model it has never heard of — or one whose price
            // moved — can supply the current rate and get a real cost estimate rather than
            // "unknown". Still no key column; those belong to ISecretStore alone.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                ALTER TABLE provider_profiles ADD COLUMN input_cost_per_million  TEXT NULL;
                ALTER TABLE provider_profiles ADD COLUMN output_cost_per_million TEXT NULL;
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        if (version < 3)
        {
            // Iteration 6: what is known about a repository, and the runs that produced it.
            //
            // Keyed by worktree root path, the same key recent_repositories already uses — one
            // repository, one profile (§0.6 fixes the scope at one repository per analysis).
            //
            // The split between the generated column and the user's columns is load-bearing:
            // regenerating a profile writes document_json and the four generated_* columns and
            // touches nothing else, which is how a manual edit survives a rerun.
            //
            // Nothing here is named for a credential. A column called secret_globs would fail
            // SqliteStoreTests.The_schema_has_no_column_that_looks_like_a_credential, which is
            // the guard working: it cannot tell a list of paths from a list of keys, and it
            // should not have to.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                CREATE TABLE project_profiles (
                    repository_path       TEXT PRIMARY KEY,
                    document_json         TEXT NULL,
                    user_notes            TEXT NOT NULL DEFAULT '',
                    custom_instructions   TEXT NOT NULL DEFAULT '',
                    excluded_globs        TEXT NULL,
                    character_budget      INTEGER NULL,
                    document_characters   INTEGER NULL,
                    generated_at_utc      TEXT NULL,
                    generated_from_commit TEXT NULL,
                    generated_by_provider TEXT NULL,
                    generated_by_model    TEXT NULL,
                    created_at_utc        TEXT NOT NULL,
                    updated_at_utc        TEXT NOT NULL
                );

                CREATE TABLE project_profile_runs (
                    id               TEXT PRIMARY KEY,
                    repository_path  TEXT NOT NULL,
                    started_at_utc   TEXT NOT NULL,
                    finished_at_utc  TEXT NULL,
                    outcome          TEXT NOT NULL,
                    failure_code     TEXT NULL,
                    provider_name    TEXT NOT NULL,
                    model            TEXT NOT NULL,
                    input_tokens     INTEGER NOT NULL,
                    output_tokens    INTEGER NOT NULL,
                    cost_usd         TEXT NULL,
                    duration_ms      INTEGER NOT NULL,
                    trace_json       TEXT NOT NULL
                );

                CREATE INDEX ix_project_profile_runs_repository
                    ON project_profile_runs (repository_path, started_at_utc DESC);
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        if (version < 4)
        {
            // Iteration 7: the analyses themselves. One row per completed run — a cancelled or
            // rejected run has nothing to store, which is what keeps §0.2.8's "nothing partial is
            // ever shown" true even after a restart.
            //
            // The document is a versioned JSON blob with the things worth querying lifted out
            // beside it: repository and created_at for "show me the latest", model and
            // schema_version because both go stale and both are how an old result is recognised as
            // one. Statistics, diagnostics and the tool trace are separate blobs rather than
            // columns because nothing filters on them and flattening them would be a schema change
            // every time the graph gains a number.
            //
            // input_tokens and output_tokens are spelled exactly as project_profile_runs spells
            // them, deliberately: The_schema_has_no_column_that_looks_like_a_credential blanks
            // those two names before it goes looking for the word "token", and a third spelling
            // would fail a guard that is right to be blunt.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                CREATE TABLE analyses (
                    id               TEXT PRIMARY KEY,
                    repository_path  TEXT NOT NULL,
                    schema_version   TEXT NOT NULL,
                    created_at_utc   TEXT NOT NULL,
                    head_commit      TEXT NULL,
                    provider_name    TEXT NOT NULL,
                    model            TEXT NOT NULL,
                    input_tokens     INTEGER NOT NULL,
                    output_tokens    INTEGER NOT NULL,
                    cost_usd         TEXT NULL,
                    duration_ms      INTEGER NOT NULL,
                    repair_rounds    INTEGER NOT NULL,
                    document_json    TEXT NOT NULL,
                    statistics_json  TEXT NOT NULL,
                    diagnostics_json TEXT NOT NULL,
                    trace_json       TEXT NOT NULL
                );

                CREATE INDEX ix_analyses_repository
                    ON analyses (repository_path, created_at_utc DESC);

                CREATE INDEX ix_analyses_model ON analyses (model);

                CREATE INDEX ix_analyses_schema_version ON analyses (schema_version);
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        if (version < 5)
        {
            // Iteration 8: two additions, both nullable, both readable by a build that predates
            // them as "not set" rather than as a failure.
            //
            // files_json is the changeset the analysis was made from — status, line counts,
            // language and project, one entry per file — because the node boxes draw those and
            // joining a stored analysis against today's working tree would print today's numbers
            // on yesterday's diagram. A row written by version 4 reads back as an empty list and
            // its boxes simply show no counts; the analysis still opens.
            //
            // context_window_tokens is the per-profile override of the bundled model table, and it
            // behaves exactly as the two cost overrides beside it do: null means "fall through to
            // the table", and the table not knowing the model means unknown, never a guess. It is
            // shown during a run and enforced nowhere — no budget consults it.
            //
            // The name says "tokens" for the same reason input_tokens does, and
            // The_schema_has_no_column_that_looks_like_a_credential blanks it before scanning, so
            // the guard keeps asking its real question without a column being renamed to something
            // less true.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                ALTER TABLE analyses ADD COLUMN files_json TEXT NULL;
                ALTER TABLE provider_profiles ADD COLUMN context_window_tokens INTEGER NULL;
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        if (version < 6)
        {
            // Iteration 10: which nodes the reviewer has marked reviewed, as a JSON array of node
            // ids. Nullable and additive for the same reason files_json was — a version-5 row reads
            // back as "nothing reviewed" rather than as a failure, and the analysis still opens.
            //
            // A column on analyses rather than a table of its own, because a mark belongs to one
            // analysis and dies with it: DELETE FROM analyses already takes it, with no foreign key
            // to remember and no orphan row to prune. The trade is that a re-run starts a fresh row
            // and therefore a fresh count, which is the honest reading of "persisted with the
            // analysis".
            //
            // Ids, not indices. §0.6 makes node identity path-derived and stable across re-runs, so
            // a mark keeps its meaning when the model rephrases a title, and Iteration 11 can switch
            // grouping mode underneath it without touching this column.
            await connection.ExecuteAsync(new CommandDefinition(
                "ALTER TABLE analyses ADD COLUMN reviewed_json TEXT NULL;",
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
