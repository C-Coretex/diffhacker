using DiffHacker.Contracts;
using DiffHacker.Core.Knowledge;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Settings;
using DiffHacker.Host.Knowledge;
using DiffHacker.Host.Rpc;
using DiffHacker.Git;
using DiffHacker.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The profile RPC surface against a real database and a real repository, with only the LLM run
/// itself faked — there is no test that reaches a provider.
/// <para>
/// The documentation half is where the important assertions are. Requirement 3 and §0.2.12 make
/// this the only write path in the product, and "cancel writes nothing" and "an overwrite is shown
/// first" are properties the interface must not be trusted to keep on its own.
/// </para>
/// </summary>
public sealed class ProfileRpcTests : IAsyncLifetime
{
    private readonly DirectoryInfo _dataDirectory =
        Directory.CreateTempSubdirectory("diffhacker-profile-rpc-");

    private TemporaryRepository _repository = null!;
    private AppDatabase _database = null!;
    private SqliteProjectProfileStore _store = null!;
    private StubProfileBuilder _builder = null!;
    private ProfileRpcTarget _target = null!;

    public ValueTask InitializeAsync()
    {
        _repository = TemporaryRepository.CreateWithCommit();
        _repository.Write("readme.md", "fixture\n");
        _repository.Commit("initial");
        _database = new AppDatabase(
            Path.Combine(_dataDirectory.FullName, "diffhacker.db"),
            NullLogger<AppDatabase>.Instance);

        _store = new SqliteProjectProfileStore(_database, TimeProvider.System);
        _builder = new StubProfileBuilder(_store);

        _target = new ProfileRpcTarget(
            _store,
            _builder,
            Git(),
            new RepositoryDocumentationWriter(NullLogger<RepositoryDocumentationWriter>.Instance),
            new RunEventNotifier(new SilentNotifier(), NullLogger<RunEventNotifier>.Instance),
            TimeProvider.System,
            NullLogger<ProfileRpcTarget>.Instance);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _repository.Dispose();

        try
        {
            _dataDirectory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public async Task A_repository_with_nothing_stored_reports_no_profile_rather_than_failing()
    {
        var state = await _target.GetAsync(Request(), TestContext.Current.CancellationToken);

        state.HasProfile.ShouldBeFalse();
        state.CharacterBudget.ShouldBe(ProfileBudget.DefaultCharacters);
        state.EffectiveExcludedGlobs.ShouldContain("**/.env");
        state.DriftSubstantial.ShouldBeFalse();
    }

    [Fact]
    public async Task Generating_returns_the_stored_profile()
    {
        var state = await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        state.HasProfile.ShouldBeTrue();
        state.Purpose.ShouldBe("A reviewer for large diffs.");
        state.Modules.ShouldHaveSingleItem().Name.ShouldBe("Core");
        state.GeneratedByModel.ShouldBe("test-model");
    }

    [Fact]
    public async Task A_failed_run_becomes_a_translatable_code_rather_than_prose()
    {
        _builder.FailureCode = ProfileFailures.UnreadableAnswer;

        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        var data = failure.ErrorData.ShouldBeOfType<RpcErrorData>();
        data.Code.ShouldBe(ProfileFailures.UnreadableAnswer);
    }

    [Fact]
    public async Task Editing_the_generated_sections_leaves_the_users_words_alone()
    {
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        await _target.SaveNotesAsync(
            new SaveProfileNotesRequest(
                characterBudget: null,
                customExcludedGlobs: ["**/*.mine"],
                customInstructions: "This is CQRS.",
                repositoryPath: _repository.Root,
                userNotes: "Kept verbatim."),
            TestContext.Current.CancellationToken);

        var edited = await _target.SaveDocumentAsync(
            new SaveProfileRequest(
                architecture: "Edited architecture.",
                documentationSources: ["README.md"],
                entryPoints: [],
                layering: "Edited layering.",
                modules: [],
                patterns: "Edited patterns.",
                purpose: "Edited purpose.",
                repositoryPath: _repository.Root,
                testLayout: "Edited tests."),
            TestContext.Current.CancellationToken);

        edited.Purpose.ShouldBe("Edited purpose.");
        edited.UserNotes.ShouldBe("Kept verbatim.");
        edited.CustomInstructions.ShouldBe("This is CQRS.");
        edited.CustomExcludedGlobs.ShouldBe(["**/*.mine"]);
    }

    [Fact]
    public async Task Regenerating_leaves_the_users_words_byte_identical()
    {
        // Requirement 5, end to end through the RPC surface.
        await _target.SaveNotesAsync(
            new SaveProfileNotesRequest(
                characterBudget: null,
                customExcludedGlobs: [],
                customInstructions: "Ignore the generated/ folder.",
                repositoryPath: _repository.Root,
                userNotes: "  Leading and trailing space kept.  "),
            TestContext.Current.CancellationToken);

        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        _builder.Purpose = "Regenerated purpose.";
        var state = await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        state.Purpose.ShouldBe("Regenerated purpose.");
        state.UserNotes.ShouldBe("  Leading and trailing space kept.  ");
        state.CustomInstructions.ShouldBe("Ignore the generated/ folder.");
    }

    [Fact]
    public async Task Editing_before_anything_is_generated_is_refused()
    {
        await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.SaveDocumentAsync(
                new SaveProfileRequest(
                    architecture: string.Empty,
                    documentationSources: [],
                    entryPoints: [],
                    layering: string.Empty,
                    modules: [],
                    patterns: string.Empty,
                    purpose: string.Empty,
                    repositoryPath: _repository.Root,
                    testLayout: string.Empty),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Drift_is_reported_once_the_repository_has_moved_on()
    {
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken))
            .DriftSubstantial.ShouldBeFalse();

        for (var i = 0; i < 30; i++)
        {
            _repository.Write($"added-{i:00}.txt", "content\n");
        }

        _repository.Commit("thirty files");

        var drifted = await _target.GetAsync(Request(), TestContext.Current.CancellationToken);

        drifted.DriftSubstantial.ShouldBeTrue();
        drifted.DriftFilesChanged.ShouldBe(30);
        drifted.DriftCommitReachable.ShouldBe(true);
    }

    [Fact]
    public async Task Previewing_shows_every_file_with_its_full_content_and_writes_nothing()
    {
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        var preview = await _target.PreviewDocumentationAsync(
            new DocumentationRequest(_repository.Root, DocumentationRequestTarget.Docs_directory),
            TestContext.Current.CancellationToken);

        preview.Files.Count.ShouldBe(3);
        preview.Files.Select(file => file.RelativePath).ShouldBe(
            ["docs/ARCHITECTURE.md", "docs/MODULES.md", "docs/CONVENTIONS.md"]);

        foreach (var file in preview.Files)
        {
            file.Content.ShouldNotBeNullOrWhiteSpace();
            file.Exists.ShouldBeFalse();
        }

        // Verification step 4: cancel and confirm nothing was written to disk.
        Directory.Exists(Path.Combine(_repository.Root, "docs")).ShouldBeFalse();
    }

    [Fact]
    public async Task Exporting_writes_exactly_the_previewed_files()
    {
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        var preview = await _target.PreviewDocumentationAsync(
            new DocumentationRequest(_repository.Root, DocumentationRequestTarget.Docs_directory),
            TestContext.Current.CancellationToken);

        var result = await _target.ExportDocumentationAsync(
            new DocumentationExportRequest(
                previewToken: preview.PreviewToken,
                repositoryPath: _repository.Root,
                target: DocumentationExportRequestTarget.Docs_directory),
            TestContext.Current.CancellationToken);

        result.WrittenPaths.ShouldBe(preview.Files.Select(file => file.RelativePath));
        result.OverwrittenPaths.ShouldBeEmpty();

        foreach (var file in preview.Files)
        {
            var absolute = Path.Combine(_repository.Root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            File.Exists(absolute).ShouldBeTrue();
            (await File.ReadAllTextAsync(absolute, TestContext.Current.CancellationToken))
                .ShouldBe(file.Content);
        }
    }

    [Fact]
    public async Task An_existing_file_is_shown_as_a_diff_before_it_is_replaced()
    {
        // Verification step 5, and the reason the preview carries a diff at all.
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        _repository.Write("ARCHITECTURE.md", "# Hand written\n\nSomeone wrote this.\n");

        var preview = await _target.PreviewDocumentationAsync(
            new DocumentationRequest(_repository.Root, DocumentationRequestTarget.Repository_root),
            TestContext.Current.CancellationToken);

        var architecture = preview.Files.Single(file => file.RelativePath == "ARCHITECTURE.md");

        architecture.Exists.ShouldBeTrue();
        architecture.UnifiedDiff.ShouldNotBeNull().ShouldContain("-Someone wrote this.");
        architecture.ExistingIsUnreadable.ShouldNotBe(true);

        var result = await _target.ExportDocumentationAsync(
            new DocumentationExportRequest(
                previewToken: preview.PreviewToken,
                repositoryPath: _repository.Root,
                target: DocumentationExportRequestTarget.Repository_root),
            TestContext.Current.CancellationToken);

        result.OverwrittenPaths.ShouldBe(["ARCHITECTURE.md"]);
    }

    [Fact]
    public async Task An_export_with_no_preview_token_writes_nothing()
    {
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.ExportDocumentationAsync(
                new DocumentationExportRequest(
                    previewToken: "not-a-real-token",
                    repositoryPath: _repository.Root,
                    target: DocumentationExportRequestTarget.Docs_directory),
                TestContext.Current.CancellationToken));

        Directory.Exists(Path.Combine(_repository.Root, "docs")).ShouldBeFalse();
    }

    [Fact]
    public async Task An_export_whose_profile_changed_since_the_preview_writes_nothing()
    {
        // The gate is over the content, not over the intent: the user approved bytes that are no
        // longer the bytes this would write.
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        var preview = await _target.PreviewDocumentationAsync(
            new DocumentationRequest(_repository.Root, DocumentationRequestTarget.Docs_directory),
            TestContext.Current.CancellationToken);

        _builder.Purpose = "Something entirely different.";
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.ExportDocumentationAsync(
                new DocumentationExportRequest(
                    previewToken: preview.PreviewToken,
                    repositoryPath: _repository.Root,
                    target: DocumentationExportRequestTarget.Docs_directory),
                TestContext.Current.CancellationToken));

        Directory.Exists(Path.Combine(_repository.Root, "docs")).ShouldBeFalse();
    }

    [Fact]
    public async Task Documentation_cannot_be_previewed_before_a_profile_exists()
    {
        await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.PreviewDocumentationAsync(
                new DocumentationRequest(_repository.Root, DocumentationRequestTarget.Docs_directory),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Forgetting_a_profile_leaves_nothing_behind()
    {
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        var state = await _target.DeleteAsync(Request(), TestContext.Current.CancellationToken);

        state.HasProfile.ShouldBeFalse();
        state.UserNotes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_profile_survives_the_application_restarting()
    {
        // Verification step 7, minus the window: the database file is the whole of what carries
        // across, so a second store over the same file is a faithful restart.
        await _target.GenerateAsync(Request(), TestContext.Current.CancellationToken);
        await _target.SaveNotesAsync(
            new SaveProfileNotesRequest(
                characterBudget: 20_000,
                customExcludedGlobs: ["**/*.mine"],
                customInstructions: "This is CQRS.",
                repositoryPath: _repository.Root,
                userNotes: "Survives."),
            TestContext.Current.CancellationToken);

        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        _database = new AppDatabase(
            Path.Combine(_dataDirectory.FullName, "diffhacker.db"),
            NullLogger<AppDatabase>.Instance);

        var reopened = new SqliteProjectProfileStore(_database, TimeProvider.System);
        var profile = (await reopened.GetAsync(_repository.Root, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        profile.Document.ShouldNotBeNull().Purpose.ShouldBe("A reviewer for large diffs.");
        profile.Provenance.ShouldNotBeNull().CommitSha.ShouldBe(_repository.HeadSha());
        profile.Provenance.GeneratedAtUtc.ShouldNotBe(default);
        profile.UserNotes.ShouldBe("Survives.");
        profile.CustomInstructions.ShouldBe("This is CQRS.");
        profile.CharacterBudget.ShouldBe(20_000);
    }

    private ProfileRequest Request() => new(_repository.Root);

    /// <summary>
    /// A real git client. The profile surface reads history — the head commit it records, and the
    /// comparison behind drift — so faking git here would fake the thing under test.
    /// </summary>
    private static GitClient Git()
    {
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);
        var environment = new GitEnvironment(runner, NullLogger<GitEnvironment>.Instance);

        return new GitClient(runner, environment, NullLogger<GitClient>.Instance);
    }

    /// <summary>
    /// Stands in for the LLM run. No test in this repository reaches a provider, and what is under
    /// test here is the RPC surface around the run rather than the run itself.
    /// </summary>
    private sealed class StubProfileBuilder(IProjectProfileStore store) : IProfileBuilder
    {
        public string Purpose { get; set; } = "A reviewer for large diffs.";

        public string? FailureCode { get; set; }

        public async Task<ProfileBuildResult> BuildAsync(
            string repositoryPath,
            IProgress<LlmRunEvent>? progress,
            CancellationToken cancellationToken)
        {
            var run = new ProfileRun
            {
                Id = Guid.NewGuid().ToString("N"),
                RepositoryPath = repositoryPath,
                StartedAtUtc = DateTimeOffset.UnixEpoch,
                Outcome = FailureCode is null ? ProfileRunOutcome.Completed : ProfileRunOutcome.Failed,
                FailureCode = FailureCode,
                ProviderDisplayName = "Test provider",
                Model = "test-model",
            };

            if (FailureCode is not null)
            {
                return new ProfileBuildResult
                {
                    Outcome = ProfileRunOutcome.Failed,
                    Run = run,
                    FailureCode = FailureCode,
                };
            }

            // The domain document, not the generated contract of the same name: this is what the
            // store keeps, where the contract is what the model answers in.
            var document = new Core.Knowledge.ProjectProfileDocument
            {
                Purpose = Purpose,
                Architecture = "A host and a renderer.",
                Modules = [new ProjectModule { Name = "Core", Path = "src/Core", Summary = "Domain." }],
                Layering = "Core references nothing above it.",
                Patterns = "- Codes, not prose.",
                EntryPoints = [new ProjectEntryPoint { Path = "src/Program.cs", Purpose = "Composition root." }],
                TestLayout = "xUnit under tests/.",
                DocumentationSources = ["README.md"],
            };

            var head = await Git()
                .GetHeadCommitAsync(repositoryPath, cancellationToken)
                .ConfigureAwait(false);

            await store.SaveGeneratedAsync(
                repositoryPath,
                document,
                new ProfileProvenance
                {
                    CommitSha = head,
                    GeneratedAtUtc = DateTimeOffset.UtcNow,
                    ProviderDisplayName = "Test provider",
                    Model = "test-model",
                    DocumentCharacters = ProfileBudget.Measure(document),
                },
                cancellationToken).ConfigureAwait(false);

            return new ProfileBuildResult
            {
                Outcome = ProfileRunOutcome.Completed,
                Run = run,
                Profile = await store.GetAsync(repositoryPath, cancellationToken).ConfigureAwait(false),
            };
        }
    }

    /// <summary>The notifier needs somewhere to go; nothing here asserts on notifications.</summary>
    private sealed class SilentNotifier : IRpcNotifier
    {
        public Task NotifyAsync(string method, object payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
