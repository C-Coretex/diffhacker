using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Storage.Tests;

/// <summary>
/// The upgrades a fresh database never exercises: v3 to v4, v4 to v5, and v4 to v6.
/// <para>
/// Each builds the older schema by hand rather than by running the current migration with the
/// version number rewound — that would test nothing, because the columns would already be there.
/// </para>
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

    [Fact]
    public async Task An_analysis_stored_before_the_per_file_facts_existed_still_opens()
    {
        // The half of a migration that matters most here. An analysis written by Iteration 7 has no
        // files_json, and the only acceptable behaviour is that it opens with no line counts on its
        // boxes — not that it fails to open, and not that today's working tree is read instead.
        await BuildVersion4DatabaseAsync(TestContext.Current.CancellationToken);

        await using (var database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance))
        {
            var store = new SqliteAnalysisStore(database);

            var stored = (await store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
                .ShouldNotBeNull();

            stored.Id.ShouldBe("old-analysis");
            stored.SchemaVersion.ShouldBe("1.7.0");
            stored.Document.Nodes.ShouldNotBeEmpty();

            // The point: no per-file facts, and the analysis opens anyway.
            stored.ChangedFiles.ShouldBeEmpty();

            // And a new analysis saved into the upgraded database carries its facts as normal.
            await store.SaveAsync(SqliteAnalysisStoreTests.Sample(), TestContext.Current.CancellationToken);

            (await store.FindAsync("analysis1", TestContext.Current.CancellationToken))
                .ShouldNotBeNull().ChangedFiles.ShouldNotBeEmpty();
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task An_analysis_stored_before_reviewed_marks_existed_still_opens_and_can_be_marked()
    {
        // Iteration 10's half of the same question. An analysis written by Iteration 7 has no
        // reviewed_json, and the only acceptable behaviour is that it opens with nothing marked and
        // then accepts a mark like any other — not that it fails to open, and not that the column's
        // absence is mistaken for a corrupt row.
        await BuildVersion4DatabaseAsync(TestContext.Current.CancellationToken);

        await using (var database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance))
        {
            var store = new SqliteAnalysisStore(database);

            var stored = (await store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
                .ShouldNotBeNull();

            stored.ReviewedNodeIds.ShouldBeEmpty();

            var marks = await store.SetNodesReviewedAsync(
                stored.Id,
                [stored.Document.Nodes[0].Id],
                reviewed: true,
                TestContext.Current.CancellationToken);

            marks.ShouldBe([stored.Document.Nodes[0].Id]);

            (await store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
                .ShouldNotBeNull()
                .ReviewedNodeIds.ShouldBe([stored.Document.Nodes[0].Id]);
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task A_provider_saved_before_context_windows_existed_has_no_override()
    {
        await BuildVersion4DatabaseAsync(TestContext.Current.CancellationToken);

        await using (var database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance))
        {
            var profiles = new SqliteProviderProfileStore(database);

            var profile = (await profiles.FindAsync("p1", TestContext.Current.CancellationToken))
                .ShouldNotBeNull();

            // Null, so the bundled table is consulted — exactly what a profile with no cost
            // override already does.
            profile.ContextWindowTokens.ShouldBeNull();
            profile.ContextWindowOverride.ShouldBeNull();
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// The schema exactly as Iteration 7 left it, with one analysis and one provider already in it.
    /// </summary>
    private async Task BuildVersion4DatabaseAsync(CancellationToken cancellationToken)
    {
        await BuildVersion3DatabaseAsync(cancellationToken);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _directory.DatabaseFile,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // The two documents are serialised by the same code the store uses rather than written out
        // by hand: a hand-written blob that no longer deserialises would fail this test for a
        // reason that has nothing to do with the migration under test.
        var sample = SqliteAnalysisStoreTests.Sample();

        await using var command = connection.CreateCommand();
        command.CommandText = Version4Schema;
        command.Parameters.AddWithValue(
            "@documentJson",
            JsonSerializer.Serialize(sample.Document, StorageJson.Options));
        command.Parameters.AddWithValue(
            "@statisticsJson",
            JsonSerializer.Serialize(sample.Statistics, StorageJson.Options));

        await command.ExecuteNonQueryAsync(cancellationToken);
        await connection.CloseAsync();
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

    /// <summary>
    /// What Iteration 7's migration added, plus one row in each of the two tables Iteration 8
    /// alters — so the upgrade has something to preserve rather than only something to create.
    /// </summary>
    private const string Version4Schema =
        """
        UPDATE schema_version SET version = 4;

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

        INSERT INTO analyses
            (id, repository_path, schema_version, created_at_utc, head_commit, provider_name,
             model, input_tokens, output_tokens, cost_usd, duration_ms, repair_rounds,
             document_json, statistics_json, diagnostics_json, trace_json)
        VALUES ('old-analysis', '/repo', '1.7.0', '2026-05-01T00:00:00.0000000+00:00',
                'head0001', 'Test', 'gpt-4o', 1000, 200, '0.25', 42000, 0,
                @documentJson, @statisticsJson, '[]',
                '{"toolCalls":[],"progressMessages":[]}');

        INSERT INTO provider_profiles
            (id, provider_type, display_name, model, base_url, created_at_utc, updated_at_utc)
        VALUES ('p1', 'openai', 'Old profile', 'gpt-4o', NULL,
                '2026-05-01T00:00:00.0000000+00:00', '2026-05-01T00:00:00.0000000+00:00');
        """;
}
