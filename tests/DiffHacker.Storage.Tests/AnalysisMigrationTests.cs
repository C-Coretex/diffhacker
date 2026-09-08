using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Storage.Tests;

/// <summary>
/// The v3 to v4 upgrade, which a fresh database never exercises.
/// </summary>
public sealed class AnalysisMigrationTests : IDisposable
{
    private readonly TemporaryDataDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task An_existing_v3_database_gains_the_analyses_table_without_losing_its_rows()
    {
        await BuildVersion3DatabaseAsync(TestContext.Current.CancellationToken);

        await using (var database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance))
        {
            var store = new SqliteAnalysisStore(database);

            await store.SaveAsync(
                SqliteAnalysisStoreTests.Sample(),
                TestContext.Current.CancellationToken);

            (await store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
                .ShouldNotBeNull().Id.ShouldBe("analysis1");

            // And the profile the previous iteration stored is still there afterwards, which is
            // the half of a migration that is easy to get wrong and easy not to notice.
            var profiles = new SqliteProjectProfileStore(database, TimeProvider.System);

            (await profiles.GetAsync("/repo", TestContext.Current.CancellationToken))
                .ShouldNotBeNull().UserNotes.ShouldBe("kept across the upgrade");
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// The schema exactly as Iteration 6 left it, written by hand. Building it from the current
    /// migration and then rewinding the version number would test nothing: the table would already
    /// be there.
    /// </summary>
    private async Task BuildVersion3DatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _directory.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE schema_version (version INTEGER NOT NULL);
            INSERT INTO schema_version (version) VALUES (3);

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
                model_suggestions TEXT NULL,
                input_cost_per_million  TEXT NULL,
                output_cost_per_million TEXT NULL
            );

            CREATE TABLE app_settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE INDEX ix_recent_repositories_last_opened
                ON recent_repositories (last_opened_utc DESC);

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

            INSERT INTO project_profiles
                (repository_path, user_notes, custom_instructions, created_at_utc, updated_at_utc)
            VALUES ('/repo', 'kept across the upgrade', '',
                    '2026-05-01T00:00:00.0000000+00:00', '2026-05-01T00:00:00.0000000+00:00');
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
    }
}
