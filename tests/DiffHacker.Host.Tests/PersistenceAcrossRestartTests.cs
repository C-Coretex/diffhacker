using System.Text.Json;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Secrets;
using DiffHacker.Git;
using DiffHacker.Host.Rpc;
using DiffHacker.Storage;
using DiffHacker.Storage.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Host.Tests;

/// <summary>
/// Iteration 2's bar, demonstrated rather than assumed: add a repository, configure a provider,
/// close the application, start it again, and find everything still there.
/// <para>
/// Everything here is real except the window — real git, real SQLite, the real secret store on
/// whatever platform this runs on. The second half constructs a completely fresh object graph
/// over the same data directory, which is what a restart actually is.
/// </para>
/// </summary>
public sealed class PersistenceAcrossRestartTests : IDisposable
{
    private const string ApiKey = "sk-persisted-across-restart-0123456789";

    private readonly AppPaths _paths = new(Directory.CreateTempSubdirectory("diffhacker-restart-").FullName);

    public PersistenceAcrossRestartTests() => _paths.EnsureCreated();

    [Fact]
    public async Task A_repository_and_a_provider_survive_a_restart()
    {
        var repositoryPath = FindRepositoryRoot();
        string profileId;

        // ---- First run -------------------------------------------------------------------
        await using (var session = new Session(_paths))
        {
            var opened = await session.Repositories.OpenAsync(
                new Contracts.OpenRepositoryRequest(repositoryPath),
                TestContext.Current.CancellationToken);

            opened.Repository.Name.ShouldBe("diffhacker");

            var saved = await session.Providers.SaveAsync(
                new Contracts.SaveProviderRequest(
                    apiKey: ApiKey,
                    baseUrl: null,
                    displayName: "Work account",
                    id: null,
                    model: "gpt-4o",
                    providerType: Contracts.SaveProviderRequestProviderType.Openai),
                TestContext.Current.CancellationToken);

            profileId = saved.Profiles[0].Id;
            saved.ActiveProfileId.ShouldBe(profileId);

            var second = await session.Providers.SaveAsync(
                new Contracts.SaveProviderRequest(
                    apiKey: "sk-second-provider-key-0000000000",
                    baseUrl: null,
                    displayName: "Personal",
                    id: null,
                    model: "claude-sonnet-4",
                    providerType: Contracts.SaveProviderRequestProviderType.Anthropic),
                TestContext.Current.CancellationToken);

            second.Profiles.Count.ShouldBe(2);

            // Deliberately switch the active provider, so the restart has something to get wrong.
            var activated = await session.Providers.SetActiveAsync(
                new Contracts.ProviderIdRequest(second.Profiles.Single(p => p.DisplayName == "Personal").Id),
                TestContext.Current.CancellationToken);

            profileId = activated.ActiveProfileId!;
        }

        // ---- Restart ---------------------------------------------------------------------
        await using (var session = new Session(_paths))
        {
            var recents = await session.Repositories.ListRecentAsync(TestContext.Current.CancellationToken);

            var entry = recents.Entries.ShouldHaveSingleItem();
            entry.Path.ShouldBe(repositoryPath);
            entry.Name.ShouldBe("diffhacker");
            entry.Available.ShouldBeTrue();

            var providers = await session.Providers.ListAsync(TestContext.Current.CancellationToken);

            providers.Profiles.Count.ShouldBe(2);
            providers.ActiveProfileId.ShouldBe(profileId, "The chosen provider must survive a restart.");
            providers.Profiles.Single(p => p.Id == profileId).IsActive.ShouldBeTrue();

            foreach (var profile in providers.Profiles)
            {
                profile.HasApiKey.ShouldBeTrue("Keys are in the secret store, not lost with the process.");
            }

            // The key itself is still readable by the host, and still absent from the wire.
            var stored = await session.Secrets.GetAsync(
                LlmProviderProfile.SecretName(providers.Profiles.Single(p => p.DisplayName == "Work account").Id),
                TestContext.Current.CancellationToken);

            stored.ShouldBe(ApiKey);
            JsonSerializer.Serialize(providers).ShouldNotContain(ApiKey);
        }
    }

    [Fact]
    public async Task Opening_a_subdirectory_records_the_worktree_root_not_the_subdirectory()
    {
        var repositoryPath = FindRepositoryRoot();

        await using var session = new Session(_paths);

        var opened = await session.Repositories.OpenAsync(
            new Contracts.OpenRepositoryRequest(Path.Combine(repositoryPath, "src", "DiffHacker.Host")),
            TestContext.Current.CancellationToken);

        opened.NormalizedFromSubdirectory.ShouldBeTrue();
        opened.Repository.Path.ShouldBe(repositoryPath);

        var recents = await session.Repositories.ListRecentAsync(TestContext.Current.CancellationToken);
        recents.Entries.ShouldHaveSingleItem().Path.ShouldBe(
            repositoryPath,
            "A subdirectory must not become its own entry in the recent list.");
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_paths.DataDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    /// <summary>Walks up to this repository, which is a real working tree to point at.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "DiffHacker.Host")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>One run of the application, minus the window.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly AppDatabase _database;

        public Session(AppPaths paths)
        {
            _database = new AppDatabase(paths.DatabaseFile, NullLogger<AppDatabase>.Instance);

            Secrets = SecretStoreFactory.Create(
                paths.SecretsFile,
                paths.MasterKeyFile,
                paths.SecretSaltFile,
                NullLogger.Instance);

            var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);
            var environment = new GitEnvironment(runner, NullLogger<GitEnvironment>.Instance);
            var locator = new RepositoryLocator(runner, environment, NullLogger<RepositoryLocator>.Instance);

            Repositories = new RepositoryRpcTarget(
                new FakeAppShell(),
                locator,
                new SqliteRecentRepositoryStore(_database),
                NullLogger<RepositoryRpcTarget>.Instance);

            Providers = new ProviderRpcTarget(
                new SqliteProviderProfileStore(_database),
                Secrets,
                new UnusedConnectionTester(),
                NullLogger<ProviderRpcTarget>.Instance);
        }

        public ISecretStore Secrets { get; }

        public RepositoryRpcTarget Repositories { get; }

        public ProviderRpcTarget Providers { get; }

        public async ValueTask DisposeAsync()
        {
            (Secrets as IDisposable)?.Dispose();
            await _database.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }

        /// <summary>Persistence is what is under test here; nothing calls a provider.</summary>
        private sealed class UnusedConnectionTester : IProviderConnectionTester
        {
            public ValueTask<ProviderConnectionResult> TestAsync(
                LlmProviderProfile profile,
                string apiKey,
                CancellationToken cancellationToken) =>
                throw new NotSupportedException("This test never reaches a provider.");
        }
    }
}
