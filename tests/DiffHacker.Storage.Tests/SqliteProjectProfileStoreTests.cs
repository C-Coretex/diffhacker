using DiffHacker.Core.Knowledge;
using DiffHacker.Core.Llm;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Storage.Tests;

public sealed class SqliteProjectProfileStoreTests : IAsyncLifetime
{
    private const string Repository = "/repo";

    private readonly TemporaryDataDirectory _directory = new();
    private AppDatabase _database = null!;
    private SqliteProjectProfileStore _store = null!;

    public ValueTask InitializeAsync()
    {
        _database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance);
        _store = new SqliteProjectProfileStore(_database, TimeProvider.System);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        _directory.Dispose();
    }

    [Fact]
    public async Task A_repository_nothing_is_known_about_has_no_profile()
    {
        (await _store.GetAsync(Repository, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_generated_document_comes_back_with_its_provenance()
    {
        await _store.SaveGeneratedAsync(
            Repository, Document(), Provenance(), TestContext.Current.CancellationToken);

        var profile = (await _store.GetAsync(Repository, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        profile.Document.ShouldNotBeNull().Purpose.ShouldBe("A reviewer for large diffs.");
        profile.Document.Modules.ShouldHaveSingleItem().RelatedModules.ShouldBe(["Other"]);
        profile.Provenance.ShouldNotBeNull().CommitSha.ShouldBe("abc123");
        profile.Provenance.DocumentCharacters.ShouldBe(1234);
    }

    [Fact]
    public async Task Custom_instructions_can_be_stored_before_a_profile_exists()
    {
        // "Ignore the generated/ folder" is worth writing down on day one, and requiring a profile
        // first would mean requiring an expensive run before a free sentence.
        await _store.SaveUserSectionsAsync(
            Repository, "notes", "ignore generated/", [], null, TestContext.Current.CancellationToken);

        var profile = (await _store.GetAsync(Repository, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        profile.Document.ShouldBeNull();
        profile.CustomInstructions.ShouldBe("ignore generated/");
        profile.UserNotes.ShouldBe("notes");
    }

    [Fact]
    public async Task Regenerating_replaces_the_document_and_leaves_the_users_words_byte_identical()
    {
        // The single most important test in this file: requirement 5 in one assertion.
        const string Notes = "The Legacy project is being deleted.\n\n  Trailing space kept.  ";

        await _store.SaveUserSectionsAsync(
            Repository, Notes, "This is CQRS.", ["**/*.mine"], 20_000, TestContext.Current.CancellationToken);

        await _store.SaveGeneratedAsync(
            Repository, Document(), Provenance(), TestContext.Current.CancellationToken);

        await _store.SaveGeneratedAsync(
            Repository,
            Document() with { Purpose = "Something else entirely." },
            Provenance() with { CommitSha = "def456" },
            TestContext.Current.CancellationToken);

        var profile = (await _store.GetAsync(Repository, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        profile.Document.ShouldNotBeNull().Purpose.ShouldBe("Something else entirely.");
        profile.Provenance.ShouldNotBeNull().CommitSha.ShouldBe("def456");

        profile.UserNotes.ShouldBe(Notes);
        profile.CustomInstructions.ShouldBe("This is CQRS.");
        profile.CustomExcludedGlobs.ShouldBe(["**/*.mine"]);
        profile.CharacterBudget.ShouldBe(20_000);
    }

    [Fact]
    public async Task Saving_the_users_sections_leaves_the_generated_document_alone()
    {
        await _store.SaveGeneratedAsync(
            Repository, Document(), Provenance(), TestContext.Current.CancellationToken);

        await _store.SaveUserSectionsAsync(
            Repository, "notes", "instructions", [], null, TestContext.Current.CancellationToken);

        var profile = (await _store.GetAsync(Repository, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        profile.Document.ShouldNotBeNull().Purpose.ShouldBe("A reviewer for large diffs.");
        profile.Provenance.ShouldNotBeNull().CommitSha.ShouldBe("abc123");
    }

    [Fact]
    public async Task An_edited_document_keeps_the_provenance_of_the_run_that_produced_it()
    {
        await _store.SaveGeneratedAsync(
            Repository, Document(), Provenance(), TestContext.Current.CancellationToken);

        await _store.SaveEditedDocumentAsync(
            Repository,
            Document() with { Purpose = "Corrected by hand." },
            TestContext.Current.CancellationToken);

        var profile = (await _store.GetAsync(Repository, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        profile.Document.ShouldNotBeNull().Purpose.ShouldBe("Corrected by hand.");

        // It still came from that run and that commit. What has been typed into it since is a
        // different fact, and not one this row records.
        profile.Provenance.ShouldNotBeNull().CommitSha.ShouldBe("abc123");

        // The character count is remeasured, because the budget is about what is stored now.
        profile.Provenance.DocumentCharacters.ShouldNotBe(1234);
    }

    [Fact]
    public async Task A_custom_glob_that_is_already_built_in_is_not_stored_twice()
    {
        await _store.SaveUserSectionsAsync(
            Repository, string.Empty, string.Empty, ["**/.env", "  ", "**/*.mine"], null,
            TestContext.Current.CancellationToken);

        var profile = (await _store.GetAsync(Repository, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        profile.CustomExcludedGlobs.ShouldBe(["**/*.mine"]);
        profile.EffectiveExcludedGlobs.ShouldContain("**/.env");
        profile.EffectiveExcludedGlobs.ShouldContain("**/*.mine");
    }

    [Fact]
    public async Task Forgetting_a_profile_removes_the_users_sections_with_it()
    {
        await _store.SaveGeneratedAsync(
            Repository, Document(), Provenance(), TestContext.Current.CancellationToken);
        await _store.SaveUserSectionsAsync(
            Repository, "notes", "instructions", [], null, TestContext.Current.CancellationToken);

        await _store.DeleteAsync(Repository, TestContext.Current.CancellationToken);

        (await _store.GetAsync(Repository, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Two_repositories_keep_separate_profiles()
    {
        await _store.SaveGeneratedAsync("/one", Document(), Provenance(), TestContext.Current.CancellationToken);
        await _store.SaveGeneratedAsync(
            "/two",
            Document() with { Purpose = "Different project." },
            Provenance(),
            TestContext.Current.CancellationToken);

        (await _store.GetAsync("/one", TestContext.Current.CancellationToken))!
            .Document!.Purpose.ShouldBe("A reviewer for large diffs.");
        (await _store.GetAsync("/two", TestContext.Current.CancellationToken))!
            .Document!.Purpose.ShouldBe("Different project.");
    }

    [Fact]
    public async Task A_run_is_kept_with_its_ordered_tool_trace()
    {
        await _store.RecordRunAsync(Run("r1"), TestContext.Current.CancellationToken);

        var run = (await _store.ListRunsAsync(Repository, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        run.Outcome.ShouldBe(ProfileRunOutcome.Completed);
        run.ToolCalls.Select(call => call.ToolName).ShouldBe(["get_project_profile", "read_file"]);
        run.ToolCalls[1].ResultPreview.ShouldBe("# DiffHacker");
        run.ProgressMessages.ShouldBe(["Reading the README"]);
        run.Usage.InputTokens.ShouldBe(1000);
        run.Usage.EstimatedCostUsd.ShouldBe(0.0125m);
        run.Duration.ShouldBe(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task A_cost_that_was_never_known_stays_unknown_rather_than_becoming_zero()
    {
        await _store.RecordRunAsync(
            Run("r1") with { Usage = new LlmUsage { InputTokens = 5, OutputTokens = 5 } },
            TestContext.Current.CancellationToken);

        var run = (await _store.ListRunsAsync(Repository, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        run.Usage.EstimatedCostUsd.ShouldBeNull();
    }

    [Fact]
    public async Task Runs_come_back_most_recent_first_and_the_history_is_capped()
    {
        for (var i = 0; i < 14; i++)
        {
            await _store.RecordRunAsync(
                Run($"r{i:00}") with { StartedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(i) },
                TestContext.Current.CancellationToken);
        }

        var runs = await _store.ListRunsAsync(Repository, TestContext.Current.CancellationToken);

        runs.Count.ShouldBe(10);
        runs[0].Id.ShouldBe("r13");
        runs[^1].Id.ShouldBe("r04");
    }

    [Fact]
    public async Task One_repositorys_runs_do_not_prune_anothers()
    {
        await _store.RecordRunAsync(Run("keep") with { RepositoryPath = "/other" }, TestContext.Current.CancellationToken);

        for (var i = 0; i < 14; i++)
        {
            await _store.RecordRunAsync(
                Run($"r{i:00}") with { StartedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(i) },
                TestContext.Current.CancellationToken);
        }

        (await _store.ListRunsAsync("/other", TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Id.ShouldBe("keep");
    }

    private static ProjectProfileDocument Document() => new()
    {
        Purpose = "A reviewer for large diffs.",
        Architecture = "A host and a renderer.",
        Modules = [new ProjectModule { Name = "Core", Path = "src/Core", Summary = "Domain.", RelatedModules = ["Other"] }],
        Layering = "Core references nothing above it.",
        Patterns = "- Codes, not prose.",
        EntryPoints = [new ProjectEntryPoint { Path = "src/Program.cs", Purpose = "Composition root." }],
        TestLayout = "xUnit under tests/.",
        DocumentationSources = ["README.md"],
    };

    private static ProfileProvenance Provenance() => new()
    {
        CommitSha = "abc123",
        GeneratedAtUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        ProviderDisplayName = "Test provider",
        Model = "test-model",
        DocumentCharacters = 1234,
    };

    private static ProfileRun Run(string id) => new()
    {
        Id = id,
        RepositoryPath = Repository,
        StartedAtUtc = DateTimeOffset.UnixEpoch,
        FinishedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(12),
        Outcome = ProfileRunOutcome.Completed,
        ProviderDisplayName = "Test provider",
        Model = "test-model",
        Usage = new LlmUsage
        {
            InputTokens = 1000,
            OutputTokens = 250,
            IsReported = true,
            EstimatedCostUsd = 0.0125m,
        },
        Duration = TimeSpan.FromSeconds(12),
        ToolCalls =
        [
            new LlmToolCallRecord
            {
                Ordinal = 1,
                Turn = 1,
                ToolName = "get_project_profile",
                ArgumentsPreview = "{}",
                ResultBytes = 60,
                ResultPreview = "No profile has been stored",
                Duration = TimeSpan.FromMilliseconds(2),
            },
            new LlmToolCallRecord
            {
                Ordinal = 2,
                Turn = 2,
                ToolName = "read_file",
                ArgumentsPreview = "{\"path\":\"README.md\"}",
                ResultBytes = 4096,
                ResultPreview = "# DiffHacker",
                Duration = TimeSpan.FromMilliseconds(11),
            },
        ],
        ProgressMessages = ["Reading the README"],
    };
}

/// <summary>
/// The v2 to v3 upgrade, which is the case a fresh database never exercises.
/// </summary>
public sealed class ProjectProfileMigrationTests : IDisposable
{
    private readonly TemporaryDataDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task An_existing_v2_database_gains_the_profile_tables_without_losing_its_rows()
    {
        await BuildVersion2DatabaseAsync(TestContext.Current.CancellationToken);

        await using (var database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance))
        {
            var store = new SqliteProjectProfileStore(database, TimeProvider.System);

            await store.SaveUserSectionsAsync(
                "/repo", "notes", "instructions", [], null, TestContext.Current.CancellationToken);

            (await store.GetAsync("/repo", TestContext.Current.CancellationToken))
                .ShouldNotBeNull().UserNotes.ShouldBe("notes");

            await using var connection = await database.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM recent_repositories;";

            (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string)
                .ShouldBe("kept across the upgrade");
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// The schema exactly as Iteration 4 left it, written by hand. Building it from the current
    /// migration and then rewinding the version number would test nothing: the tables would
    /// already be there.
    /// </summary>
    private async Task BuildVersion2DatabaseAsync(CancellationToken cancellationToken)
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
            INSERT INTO schema_version (version) VALUES (2);

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

            INSERT INTO recent_repositories (path, name, last_opened_utc)
            VALUES ('/repo', 'kept across the upgrade', '2026-05-01T00:00:00.0000000+00:00');
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
    }
}
