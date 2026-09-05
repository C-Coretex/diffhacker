using System.Text;
using DiffHacker.Core.Providers;
using DiffHacker.Storage.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Storage.Tests;

public sealed class SqliteRecentRepositoryStoreTests : IAsyncLifetime
{
    private readonly TemporaryDataDirectory _directory = new();
    private AppDatabase _database = null!;
    private SqliteRecentRepositoryStore _store = null!;

    public ValueTask InitializeAsync()
    {
        _database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance);
        _store = new SqliteRecentRepositoryStore(_database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        _directory.Dispose();
    }

    [Fact]
    public async Task Entries_come_back_most_recently_opened_first()
    {
        await _store.RememberAsync("/one", "one", TestContext.Current.CancellationToken);
        await _store.RememberAsync("/two", "two", TestContext.Current.CancellationToken);
        await _store.RememberAsync("/three", "three", TestContext.Current.CancellationToken);

        var entries = await _store.ListAsync(TestContext.Current.CancellationToken);

        entries.Select(entry => entry.Path).ShouldBe(["/three", "/two", "/one"]);
    }

    [Fact]
    public async Task Reopening_a_repository_moves_it_to_the_front_without_duplicating_it()
    {
        await _store.RememberAsync("/one", "one", TestContext.Current.CancellationToken);
        await _store.RememberAsync("/two", "two", TestContext.Current.CancellationToken);
        await _store.RememberAsync("/one", "one renamed", TestContext.Current.CancellationToken);

        var entries = await _store.ListAsync(TestContext.Current.CancellationToken);

        entries.Count.ShouldBe(2);
        entries[0].Path.ShouldBe("/one");
        entries[0].Name.ShouldBe("one renamed");
    }

    [Fact]
    public async Task Forgetting_removes_only_that_entry()
    {
        await _store.RememberAsync("/one", "one", TestContext.Current.CancellationToken);
        await _store.RememberAsync("/two", "two", TestContext.Current.CancellationToken);

        await _store.ForgetAsync("/one", TestContext.Current.CancellationToken);

        var entries = await _store.ListAsync(TestContext.Current.CancellationToken);
        entries.ShouldHaveSingleItem().Path.ShouldBe("/two");
    }

    [Fact]
    public async Task Forgetting_something_that_is_not_there_is_not_an_error()
    {
        await _store.ForgetAsync("/never-added", TestContext.Current.CancellationToken);
        (await _store.ListAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_list_is_capped_so_it_cannot_grow_without_bound()
    {
        for (var i = 0; i < 25; i++)
        {
            await _store.RememberAsync($"/repo-{i:00}", $"repo {i}", TestContext.Current.CancellationToken);
        }

        var entries = await _store.ListAsync(TestContext.Current.CancellationToken);

        entries.Count.ShouldBe(20);
        entries[0].Path.ShouldBe("/repo-24");
    }

    [Fact]
    public async Task Timestamps_survive_the_round_trip_as_UTC()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _store.RememberAsync("/one", "one", TestContext.Current.CancellationToken);

        var entry = (await _store.ListAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        entry.LastOpenedUtc.ShouldBeGreaterThan(before);
        entry.LastOpenedUtc.Offset.ShouldBe(TimeSpan.Zero);
    }
}

public sealed class SqliteProviderProfileStoreTests : IAsyncLifetime
{
    private readonly TemporaryDataDirectory _directory = new();
    private AppDatabase _database = null!;
    private SqliteProviderProfileStore _store = null!;

    public ValueTask InitializeAsync()
    {
        _database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance);
        _store = new SqliteProviderProfileStore(_database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        _directory.Dispose();
    }

    [Theory]
    [InlineData(LlmProviderType.OpenAi)]
    [InlineData(LlmProviderType.Anthropic)]
    [InlineData(LlmProviderType.Gemini)]
    [InlineData(LlmProviderType.Grok)]
    [InlineData(LlmProviderType.DeepSeek)]
    [InlineData(LlmProviderType.OpenAiCompatible)]
    public async Task Every_provider_type_round_trips(LlmProviderType type)
    {
        var profile = Profile("p1") with { ProviderType = type };
        await _store.SaveAsync(profile, TestContext.Current.CancellationToken);

        var loaded = await _store.FindAsync("p1", TestContext.Current.CancellationToken);

        loaded.ShouldNotBeNull();
        loaded!.ProviderType.ShouldBe(type);
    }

    [Fact]
    public async Task A_profile_round_trips_with_every_field()
    {
        var profile = Profile("p1") with
        {
            BaseUrl = "https://example.test/v1",
            ModelSuggestions = ["a-model", "b-model"],
        };

        await _store.SaveAsync(profile, TestContext.Current.CancellationToken);
        var loaded = (await _store.FindAsync("p1", TestContext.Current.CancellationToken))!;

        loaded.DisplayName.ShouldBe(profile.DisplayName);
        loaded.Model.ShouldBe(profile.Model);
        loaded.BaseUrl.ShouldBe("https://example.test/v1");
        loaded.ModelSuggestions.ShouldBe(["a-model", "b-model"]);
    }

    [Fact]
    public async Task Saving_an_existing_id_updates_rather_than_duplicating()
    {
        await _store.SaveAsync(Profile("p1"), TestContext.Current.CancellationToken);
        await _store.SaveAsync(
            Profile("p1") with { DisplayName = "Renamed" },
            TestContext.Current.CancellationToken);

        var all = await _store.ListAsync(TestContext.Current.CancellationToken);
        all.ShouldHaveSingleItem().DisplayName.ShouldBe("Renamed");
    }

    [Fact]
    public async Task Profiles_are_ordered_by_display_name_case_insensitively()
    {
        await _store.SaveAsync(Profile("a") with { DisplayName = "zeta" }, TestContext.Current.CancellationToken);
        await _store.SaveAsync(Profile("b") with { DisplayName = "Alpha" }, TestContext.Current.CancellationToken);

        var all = await _store.ListAsync(TestContext.Current.CancellationToken);
        all.Select(p => p.DisplayName).ShouldBe(["Alpha", "zeta"]);
    }

    [Fact]
    public async Task The_active_profile_persists_and_can_be_cleared()
    {
        await _store.SaveAsync(Profile("p1"), TestContext.Current.CancellationToken);

        (await _store.GetActiveIdAsync(TestContext.Current.CancellationToken)).ShouldBeNull();

        await _store.SetActiveIdAsync("p1", TestContext.Current.CancellationToken);
        (await _store.GetActiveIdAsync(TestContext.Current.CancellationToken)).ShouldBe("p1");

        await _store.SetActiveIdAsync(null, TestContext.Current.CancellationToken);
        (await _store.GetActiveIdAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_missing_profile_is_null_rather_than_an_error()
    {
        (await _store.FindAsync("nope", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_removes_the_profile()
    {
        await _store.SaveAsync(Profile("p1"), TestContext.Current.CancellationToken);
        await _store.DeleteAsync("p1", TestContext.Current.CancellationToken);

        (await _store.ListAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    /// <summary>
    /// The invariant that matters most in this file. CLAUDE.md §0.2.13 keeps keys out of SQLite
    /// entirely, and the honest way to check that is to read the bytes rather than the schema.
    /// </summary>
    [Fact]
    public async Task No_API_key_reaches_the_database_file()
    {
        const string apiKey = "sk-should-never-be-persisted-0123456789";

        using var directory = new TemporaryDataDirectory();
        await using var database = new AppDatabase(directory.DatabaseFile, NullLogger<AppDatabase>.Instance);
        var profiles = new SqliteProviderProfileStore(database);

        await profiles.SaveAsync(Profile("p1"), TestContext.Current.CancellationToken);
        await profiles.SetActiveIdAsync("p1", TestContext.Current.CancellationToken);

        using var secrets = new FileSecretStore(
            directory.SecretsFile,
            new MachineDerivedMasterKeyProtector(directory.SaltFile),
            isFallback: true);

        await secrets.SetAsync(
            LlmProviderProfile.SecretName("p1"),
            apiKey,
            TestContext.Current.CancellationToken);

        await database.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var file in Directory.EnumerateFiles(directory.Root, "diffhacker.db*"))
        {
            var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
            Encoding.UTF8.GetString(bytes).ShouldNotContain(apiKey, Case.Sensitive);
        }
    }

    [Fact]
    public async Task The_schema_has_no_column_that_looks_like_a_credential()
    {
        // A structural guard beside the byte-level one: this catches a well-meaning future
        // migration adding an api_key column before anything is ever written to it.
        await _store.SaveAsync(Profile("p1"), TestContext.Current.CancellationToken);

        await using var connection = await _database.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table';";

        var schema = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                schema.AppendLine(reader.GetString(0));
            }
        }

        var sql = schema.ToString();
        foreach (var forbidden in new[] { "api_key", "apikey", "secret", "password", "token", "credential" })
        {
            sql.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }

    private static LlmProviderProfile Profile(string id) => new()
    {
        Id = id,
        ProviderType = LlmProviderType.OpenAi,
        DisplayName = "Test profile",
        Model = "some-model",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };
}

public sealed class AppDatabaseTests
{
    [Fact]
    public async Task Opening_the_same_database_twice_is_idempotent()
    {
        using var directory = new TemporaryDataDirectory();

        await using (var first = new AppDatabase(directory.DatabaseFile, NullLogger<AppDatabase>.Instance))
        {
            var store = new SqliteRecentRepositoryStore(first);
            await store.RememberAsync("/one", "one", TestContext.Current.CancellationToken);
        }

        // Migrations run again on the second open and must not wipe or duplicate anything.
        await using var second = new AppDatabase(directory.DatabaseFile, NullLogger<AppDatabase>.Instance);
        var entries = await new SqliteRecentRepositoryStore(second)
            .ListAsync(TestContext.Current.CancellationToken);

        entries.ShouldHaveSingleItem().Path.ShouldBe("/one");
    }

    [Fact]
    public async Task A_database_from_a_newer_build_is_refused_rather_than_misread()
    {
        using var directory = new TemporaryDataDirectory();

        await using (var database = new AppDatabase(directory.DatabaseFile, NullLogger<AppDatabase>.Instance))
        {
            await using var connection = await database.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE schema_version SET version = 999;";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        await using var reopened = new AppDatabase(directory.DatabaseFile, NullLogger<AppDatabase>.Instance);

        var thrown = await Should.ThrowAsync<StorageException>(
            async () => await reopened.OpenAsync(TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain("newer version");
    }
}
