using System.Text.Json;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Repositories;
using DiffHacker.Core.Secrets;
using DiffHacker.Core.Settings;
using DiffHacker.Host.Rpc;
using DiffHacker.Storage;
using DiffHacker.Storage.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The Iteration 2 methods driven through the real bridge with raw JSON-RPC, the same way
/// <see cref="RpcBridgeTests"/> drives the Iteration 1 ones.
/// <para>
/// Asserting on the wire rather than on return values is deliberate: the invariant that matters
/// most here — an API key never travelling back into the WebView (CLAUDE.md §0.2.13) — is a
/// property of the bytes, not of the C# types.
/// </para>
/// </summary>
public sealed class SettingsRpcTests : IAsyncLifetime
{
    private const string ApiKey = "sk-never-cross-the-bridge-0123456789";

    private readonly FakeAppShell _shell = new();
    private readonly RpcNotifier _notifier = new(NullLogger<RpcNotifier>.Instance);
    private readonly AppPaths _paths = new(Directory.CreateTempSubdirectory("diffhacker-rpc-").FullName);

    private AppDatabase _database = null!;
    private ISecretStore _secrets = null!;
    private RpcBridge _bridge = null!;
    private StubConnectionTester _tester = null!;
    private int _nextId = 1;

    public ValueTask InitializeAsync()
    {
        _paths.EnsureCreated();

        _database = new AppDatabase(_paths.DatabaseFile, NullLogger<AppDatabase>.Instance);
        _secrets = SecretStoreFactory.Create(
            _paths.SecretsFile,
            _paths.MasterKeyFile,
            _paths.SecretSaltFile,
            NullLogger.Instance);

        var profiles = new SqliteProviderProfileStore(_database);
        var recents = new SqliteRecentRepositoryStore(_database);
        _tester = new StubConnectionTester();

        _bridge = new RpcBridge(
            _shell,
            _notifier,
            [
                new EnvironmentRpcTarget(new StubGitEnvironment(), _secrets),
                new RepositoryRpcTarget(
                    _shell,
                    new StubRepositoryLocator(),
                    recents,
                    NullLogger<RepositoryRpcTarget>.Instance),
                new ProviderRpcTarget(profiles, _secrets, _tester, NullLogger<ProviderRpcTarget>.Instance),
            ],
            NullLogger<RpcBridge>.Instance);

        _bridge.Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _bridge.DisposeAsync();
        (_secrets as IDisposable)?.Dispose();
        await _database.DisposeAsync();
        _shell.Dispose();

        try
        {
            Directory.Delete(_paths.DataDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public async Task Environment_describe_reports_git_and_the_secret_backend()
    {
        var result = await CallAsync("environment.describe");

        result.GetProperty("gitAvailable").GetBoolean().ShouldBeTrue();
        result.GetProperty("gitVersion").GetString().ShouldBe("git version 2.99.0");
        result.GetProperty("secretBackend").GetString().ShouldBeOneOf(
            "windows_dpapi", "macos_keychain", "linux_libsecret", "machine_derived");
    }

    [Fact]
    public async Task Saving_a_provider_never_sends_the_key_back()
    {
        var raw = await CallRawAsync(
            "providers.save",
            $$"""{"providerType":"openai","displayName":"Work","model":"gpt-4o","apiKey":"{{ApiKey}}"}""");

        // The whole response text, not just the fields we know about: a key added to the
        // contract by accident would still be caught here.
        raw.ShouldNotContain(ApiKey);

        using var document = JsonDocument.Parse(raw);
        var profile = document.RootElement.GetProperty("result").GetProperty("profiles")[0];

        profile.GetProperty("hasApiKey").GetBoolean().ShouldBeTrue();
        profile.TryGetProperty("apiKey", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Listing_providers_never_sends_the_key_back()
    {
        await SaveProviderAsync();

        var raw = await CallRawAsync("providers.list", null);

        raw.ShouldNotContain(ApiKey);
        raw.ShouldContain("\"hasApiKey\":true");
    }

    [Fact]
    public async Task The_first_provider_saved_becomes_the_active_one()
    {
        var result = await SaveProviderAsync();

        var id = result.GetProperty("profiles")[0].GetProperty("id").GetString();
        result.GetProperty("activeProfileId").GetString().ShouldBe(id);
        result.GetProperty("profiles")[0].GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_second_provider_does_not_steal_the_active_slot()
    {
        var first = await SaveProviderAsync();
        var firstId = first.GetProperty("profiles")[0].GetProperty("id").GetString();

        var second = await CallAsync(
            "providers.save",
            """{"providerType":"anthropic","displayName":"Zeta","model":"claude","apiKey":"sk-other-key-000000"}""");

        second.GetProperty("profiles").GetArrayLength().ShouldBe(2);
        second.GetProperty("activeProfileId").GetString().ShouldBe(firstId);
    }

    [Fact]
    public async Task Editing_a_provider_without_a_key_keeps_the_stored_one()
    {
        var saved = await SaveProviderAsync();
        var id = saved.GetProperty("profiles")[0].GetProperty("id").GetString();

        var updated = await CallAsync(
            "providers.save",
            $$"""{"id":"{{id}}","providerType":"openai","displayName":"Renamed","model":"gpt-4o-mini"}""");

        var profile = updated.GetProperty("profiles")[0];
        profile.GetProperty("displayName").GetString().ShouldBe("Renamed");

        // This is what lets the form edit a profile without the key ever being sent to it first.
        profile.GetProperty("hasApiKey").GetBoolean().ShouldBeTrue();
        (await _secrets.GetAsync(LlmProviderProfile.SecretName(id!), TestContext.Current.CancellationToken))
            .ShouldBe(ApiKey);
    }

    [Fact]
    public async Task Deleting_a_provider_deletes_its_key_too()
    {
        var saved = await SaveProviderAsync();
        var id = saved.GetProperty("profiles")[0].GetProperty("id").GetString()!;

        await CallAsync("providers.delete", $$"""{"id":"{{id}}"}""");

        (await _secrets.ContainsAsync(LlmProviderProfile.SecretName(id), TestContext.Current.CancellationToken))
            .ShouldBeFalse("A removed provider must not leave its credentials behind.");
    }

    [Fact]
    public async Task An_OpenAI_compatible_provider_without_a_base_url_is_rejected_with_a_code()
    {
        var error = await CallForErrorAsync(
            "providers.save",
            """{"providerType":"openai_compatible","displayName":"Local","model":"llama","apiKey":"sk-x"}""");

        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("provider_base_url_required");
    }

    [Fact]
    public async Task An_invalid_base_url_is_rejected_with_a_code()
    {
        var error = await CallForErrorAsync(
            "providers.save",
            """{"providerType":"openai","displayName":"Work","model":"gpt-4o","baseUrl":"not a url"}""");

        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("provider_invalid_base_url");
    }

    [Fact]
    public async Task Testing_a_provider_with_no_key_is_an_actionable_error()
    {
        // Saved without an apiKey, so nothing is in the secret store for it.
        var saved = await CallAsync(
            "providers.save",
            """{"providerType":"openai","displayName":"Work","model":"gpt-4o"}""");
        var id = saved.GetProperty("profiles")[0].GetProperty("id").GetString();

        var error = await CallForErrorAsync("providers.testConnection", $$"""{"id":"{{id}}"}""");

        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("provider_key_missing");
    }

    [Fact]
    public async Task A_successful_test_verifies_the_model_and_caches_the_suggestions()
    {
        _tester.Result = ProviderConnectionResult.Success(["gpt-4o", "gpt-4o-mini"]);

        var saved = await SaveProviderAsync();
        var id = saved.GetProperty("profiles")[0].GetProperty("id").GetString();

        var result = await CallAsync("providers.testConnection", $$"""{"id":"{{id}}"}""");

        result.GetProperty("succeeded").GetBoolean().ShouldBeTrue();
        result.GetProperty("modelVerified").GetBoolean().ShouldBeTrue();
        result.GetProperty("availableModels").GetArrayLength().ShouldBe(2);

        // Requirement 4's suggestions come from here, not from a hardcoded list.
        var listed = await CallAsync("providers.list", null);
        listed.GetProperty("profiles")[0].GetProperty("modelSuggestions").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_model_the_key_cannot_reach_succeeds_but_is_not_verified()
    {
        _tester.Result = ProviderConnectionResult.Success(["o3", "o4-mini"]);

        var saved = await SaveProviderAsync();
        var id = saved.GetProperty("profiles")[0].GetProperty("id").GetString();

        var result = await CallAsync("providers.testConnection", $$"""{"id":"{{id}}"}""");

        result.GetProperty("succeeded").GetBoolean().ShouldBeTrue();
        result.GetProperty("modelVerified").GetBoolean().ShouldBeFalse(
            "The key works but the model name is a typo, and that is worth saying now.");
    }

    [Fact]
    public async Task A_provider_that_echoes_the_key_back_has_it_scrubbed_before_the_bridge()
    {
        // Providers really do include the submitted key in error bodies. This is the last line
        // of defence before it would reach the WebView.
        _tester.Result = ProviderConnectionResult.Failure(
            ProviderConnectionFailures.InvalidKey,
            $"Incorrect API key provided: {ApiKey}. Check your account.",
            401);

        var saved = await SaveProviderAsync();
        var id = saved.GetProperty("profiles")[0].GetProperty("id").GetString();

        var raw = await CallRawAsync("providers.testConnection", $$"""{"id":"{{id}}"}""");

        raw.ShouldNotContain(ApiKey);
        raw.ShouldContain("Incorrect API key provided");
        raw.ShouldContain("redacted");
    }

    [Fact]
    public async Task Browsing_returns_the_chosen_folder()
    {
        _shell.FolderPickerResult = "/chosen/path";

        var result = await CallAsync("repository.browse", """{"title":"Choose a repository"}""");

        result.GetProperty("cancelled").GetBoolean().ShouldBeFalse();
        result.GetProperty("path").GetString().ShouldBe("/chosen/path");

        // The dialog title comes from the renderer's catalogue: the host authors no prose.
        _shell.FolderPickerCalls.ShouldHaveSingleItem().Title.ShouldBe("Choose a repository");
    }

    [Fact]
    public async Task Dismissing_the_picker_is_a_result_rather_than_an_error()
    {
        _shell.FolderPickerResult = null;

        var result = await CallAsync("repository.browse", """{"title":"Choose a repository"}""");

        result.GetProperty("cancelled").GetBoolean().ShouldBeTrue();
        result.TryGetProperty("path", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Opening_a_repository_records_it_in_the_recent_list()
    {
        var opened = await CallAsync("repository.open", """{"path":"/repos/alpha"}""");

        opened.GetProperty("repository").GetProperty("name").GetString().ShouldBe("alpha");
        opened.GetProperty("normalizedFromSubdirectory").GetBoolean().ShouldBeFalse();

        var recents = await CallAsync("repository.listRecent", null);
        recents.GetProperty("entries")[0].GetProperty("path").GetString().ShouldBe("/repos/alpha");
    }

    [Fact]
    public async Task Each_rejection_reason_carries_its_own_error_code()
    {
        var error = await CallForErrorAsync("repository.open", """{"path":"/bare"}""");
        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("repository_is_bare");

        error = await CallForErrorAsync("repository.open", """{"path":"/missing"}""");
        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("repository_not_found");

        error = await CallForErrorAsync("repository.open", """{"path":"/plain"}""");
        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("repository_not_a_git_repository");
    }

    [Fact]
    public async Task An_error_carries_the_path_as_an_argument_not_as_prose()
    {
        var error = await CallForErrorAsync("repository.open", """{"path":"/missing"}""");

        error.GetProperty("data").GetProperty("args").GetProperty("path").GetString().ShouldBe("/missing");
    }

    [Fact]
    public async Task Forgetting_a_recent_repository_removes_it()
    {
        await CallAsync("repository.open", """{"path":"/repos/alpha"}""");
        await CallAsync("repository.open", """{"path":"/repos/beta"}""");

        await CallAsync("repository.forgetRecent", """{"path":"/repos/alpha"}""");

        var recents = await CallAsync("repository.listRecent", null);
        recents.GetProperty("entries").GetArrayLength().ShouldBe(1);
        recents.GetProperty("entries")[0].GetProperty("path").GetString().ShouldBe("/repos/beta");
    }

    private async Task<JsonElement> SaveProviderAsync() =>
        await CallAsync(
            "providers.save",
            $$"""{"providerType":"openai","displayName":"Work","model":"gpt-4o","apiKey":"{{ApiKey}}"}""");

    private async Task<JsonElement> CallAsync(string method, string? paramsJson = null)
    {
        using var document = JsonDocument.Parse(await CallRawAsync(method, paramsJson));
        return document.RootElement.GetProperty("result").Clone();
    }

    private async Task<JsonElement> CallForErrorAsync(string method, string? paramsJson)
    {
        using var document = JsonDocument.Parse(await CallRawAsync(method, paramsJson));
        return document.RootElement.GetProperty("error").Clone();
    }

    private async Task<string> CallRawAsync(string method, string? paramsJson)
    {
        var id = _nextId++;
        var parameters = paramsJson is null ? "[]" : $"[{paramsJson}]";

        _shell.Receive($$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{parameters}}}""");

        return await _shell.NextSentAsync(TestContext.Current.CancellationToken);
    }

    private sealed class StubGitEnvironment : IGitEnvironment
    {
        public ValueTask<GitAvailability> ProbeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new GitAvailability(true, "git version 2.99.0"));
    }

    /// <summary>
    /// Resolves a handful of fixed paths, one per acceptance rule, so the RPC layer's mapping
    /// from rejection to error code can be tested without a repository on disk.
    /// </summary>
    private sealed class StubRepositoryLocator : IRepositoryLocator
    {
        public ValueTask<RepositoryResolution> ResolveAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(path switch
            {
                "/bare" => RepositoryResolution.Rejected(RepositoryRejection.BareRepository),
                "/missing" => RepositoryResolution.Rejected(RepositoryRejection.PathNotFound),
                "/plain" => RepositoryResolution.Rejected(RepositoryRejection.NotARepository),
                "/denied" => RepositoryResolution.Rejected(RepositoryRejection.AccessDenied),
                _ => RepositoryResolution.Accepted(
                    new RepositoryDescriptor
                    {
                        Path = path,
                        Name = path[(path.LastIndexOf('/') + 1)..],
                        HasCommits = true,
                        IsLinkedWorktree = false,
                    },
                    normalized: false),
            });

        public ValueTask<bool> IsStillAvailableAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed class StubConnectionTester : IProviderConnectionTester
    {
        public ProviderConnectionResult Result { get; set; } = ProviderConnectionResult.Success([]);

        public ValueTask<ProviderConnectionResult> TestAsync(
            LlmProviderProfile profile,
            string apiKey,
            CancellationToken cancellationToken) => ValueTask.FromResult(Result);
    }
}
