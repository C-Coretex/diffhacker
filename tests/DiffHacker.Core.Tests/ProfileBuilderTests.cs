using System.Text.Json;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Knowledge;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Settings;
using DiffHacker.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Core.Tests;

/// <summary>
/// The first orchestrator in the application, tested against a fake session.
/// <para>
/// The fake is of <see cref="ILlmSession"/> rather than of a chat client, because that interface is
/// the whole of what this project knows about a provider — testing through a chat client would mean
/// testing <c>DiffHacker.Llm</c> again with different assertions.
/// </para>
/// </summary>
public sealed class ProfileBuilderTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task With_no_provider_configured_nothing_is_run_and_nothing_is_stored()
    {
        var harness = new Harness();
        harness.Providers.Profiles.Clear();

        var result = await harness.BuildAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.FailureCode.ShouldBe(ProfileFailures.NoProvider);
        harness.Store.Saved.ShouldBeNull();
        harness.Sessions.Created.ShouldBe(0);
    }

    [Fact]
    public async Task A_single_configured_provider_is_used_even_when_none_was_marked_active()
    {
        // Configuring exactly one provider and never pressing "use this" is not the same as having
        // none, and telling the user it is would be a puzzle rather than an error.
        var harness = new Harness();
        harness.Providers.ActiveId = null;

        (await harness.BuildAsync(TestContext.Current.CancellationToken)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_completed_run_stores_the_document_with_the_commit_it_was_taken_from()
    {
        var harness = new Harness();

        var result = await harness.BuildAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        harness.Store.Saved.ShouldNotBeNull().Purpose.ShouldBe("A desktop reviewer for large diffs.");

        var provenance = harness.Store.SavedProvenance.ShouldNotBeNull();
        provenance.CommitSha.ShouldBe("head0001");
        provenance.Model.ShouldBe("test-model");
        provenance.ProviderDisplayName.ShouldBe("Test provider");
        provenance.DocumentCharacters.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Generating_writes_only_the_generated_half()
    {
        // Requirement 5, structurally: the builder has no way to reach the user's columns, because
        // the only method it calls does not name them.
        var harness = new Harness();

        await harness.BuildAsync(TestContext.Current.CancellationToken);

        harness.Store.UserSectionsSaved.ShouldBeFalse();
    }

    [Fact]
    public async Task The_opening_message_names_the_repositorys_documentation()
    {
        var harness = new Harness();
        harness.Git.VisibleFiles = ["README.md", "docs/decisions.md", "src/Program.cs"];

        await harness.BuildAsync(TestContext.Current.CancellationToken);

        var conversation = harness.Sessions.Session.Conversation.ShouldNotBeNull();
        conversation.UserMessage.ShouldContain("README.md");
        conversation.UserMessage.ShouldContain("docs/decisions.md");
        conversation.UserMessage.ShouldNotContain("src/Program.cs");
    }

    [Fact]
    public async Task The_stored_custom_instructions_reach_the_prompt()
    {
        var harness = new Harness();
        harness.Store.Existing = ProjectProfile.Empty("/repo", DateTimeOffset.UnixEpoch) with
        {
            CustomInstructions = "The Legacy project is being deleted.",
        };

        await harness.BuildAsync(TestContext.Current.CancellationToken);

        harness.Sessions.Session.Conversation!.UserMessage
            .ShouldContain("The Legacy project is being deleted.");
    }

    [Fact]
    public async Task The_toolbox_is_opened_with_the_repositorys_effective_withheld_globs()
    {
        var harness = new Harness();
        harness.Store.Existing = ProjectProfile.Empty("/repo", DateTimeOffset.UnixEpoch) with
        {
            CustomExcludedGlobs = ["**/*.secretish"],
        };

        await harness.BuildAsync(TestContext.Current.CancellationToken);

        harness.Toolboxes.ExcludedGlobs.ShouldContain("**/*.secretish");
        harness.Toolboxes.ExcludedGlobs.ShouldContain("**/.env");
    }

    [Fact]
    public async Task The_run_is_recorded_with_its_tool_trace_and_what_it_spent()
    {
        var harness = new Harness();

        await harness.BuildAsync(TestContext.Current.CancellationToken);

        var run = harness.Store.Runs.ShouldHaveSingleItem();
        run.Outcome.ShouldBe(ProfileRunOutcome.Completed);
        run.ToolCalls.Select(call => call.ToolName).ShouldBe(["get_project_profile", "read_file"]);
        run.Usage.InputTokens.ShouldBe(1000);
        run.ProgressMessages.ShouldBe(["Reading the README"]);
    }

    [Fact]
    public async Task An_over_long_document_is_handed_back_once_and_stored_when_it_comes_back_shorter()
    {
        var harness = new Harness();
        harness.Store.Existing = ProjectProfile.Empty("/repo", DateTimeOffset.UnixEpoch) with
        {
            CharacterBudget = ProfileBudget.MinimumCharacters,
        };

        harness.Sessions.Answers =
        [
            Serialize(Sample.Document() with { Architecture = new string('x', 2000) }),
            Serialize(Sample.Document()),
        ];

        var result = await harness.BuildAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        harness.Sessions.Created.ShouldBe(2);
        harness.Store.Saved.ShouldNotBeNull().Architecture.ShouldNotContain("xxxx");
    }

    [Fact]
    public async Task A_document_that_is_still_too_long_after_one_repair_is_saved_rather_than_failed_or_truncated()
    {
        var harness = new Harness();
        harness.Store.Existing = ProjectProfile.Empty("/repo", DateTimeOffset.UnixEpoch) with
        {
            CharacterBudget = ProfileBudget.MinimumCharacters,
        };

        var tooLong = Serialize(Sample.Document() with { Architecture = new string('x', 2000) });
        harness.Sessions.Answers = [tooLong, tooLong];

        var result = await harness.BuildAsync(TestContext.Current.CancellationToken);

        // The character budget is the reviewer's own dial, not a runaway guard: a complete,
        // valid profile that is merely long is saved whole rather than truncated or discarded.
        result.Succeeded.ShouldBeTrue();
        harness.Store.Saved.ShouldNotBeNull().Architecture.ShouldContain("xxxx");
        harness.Store.SavedProvenance.ShouldNotBeNull().DocumentCharacters
            .ShouldBeGreaterThan(ProfileBudget.MinimumCharacters);
        harness.Store.Runs.ShouldHaveSingleItem().Outcome.ShouldBe(ProfileRunOutcome.Completed);
    }

    [Fact]
    public async Task An_answer_that_is_not_a_profile_fails_without_storing_anything()
    {
        var harness = new Harness();
        harness.Sessions.Answers = ["{\"nonsense\":true}"];

        var result = await harness.BuildAsync(TestContext.Current.CancellationToken);

        result.FailureCode.ShouldBe(ProfileFailures.UnreadableAnswer);
        harness.Store.Saved.ShouldBeNull();
    }

    [Fact]
    public async Task A_failed_run_reports_the_providers_code_and_stores_no_profile()
    {
        var harness = new Harness();
        harness.Sessions.Session.Outcome = LlmRunOutcome.ContextOverflow;
        harness.Sessions.Session.FailureCode = LlmFailures.ContextOverflow;

        var result = await harness.BuildAsync(TestContext.Current.CancellationToken);

        result.FailureCode.ShouldBe(LlmFailures.ContextOverflow);
        harness.Store.Saved.ShouldBeNull();
        harness.Store.Runs.ShouldHaveSingleItem().FailureCode.ShouldBe(LlmFailures.ContextOverflow);
    }

    [Fact]
    public async Task A_cancelled_run_reports_what_it_spent_and_stores_no_profile()
    {
        var harness = new Harness();
        harness.Sessions.Session.ThrowCancellation = true;

        using var cancellation = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await harness.BuildAsync(cancellation.Token));

        harness.Store.Saved.ShouldBeNull();

        var run = harness.Store.Runs.ShouldHaveSingleItem();
        run.Outcome.ShouldBe(ProfileRunOutcome.Cancelled);

        // The tokens were spent whether or not a profile came out of them.
        run.Usage.InputTokens.ShouldBe(1000);
        run.ToolCalls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_answer_is_asked_for_in_the_shape_the_schema_describes()
    {
        var harness = new Harness();

        await harness.BuildAsync(TestContext.Current.CancellationToken);

        var format = harness.Sessions.Session.Conversation!.ResponseFormat.ShouldNotBeNull();
        format.SchemaName.ShouldBe("project_profile");
        format.SchemaJson.ShouldContain("\"title\": \"ProjectProfileDocument\"");
    }

    private static string Serialize(ProjectProfileDocument document) =>
        JsonSerializer.Serialize(document, Json);

    private sealed class Harness
    {
        public FakeProviderStore Providers { get; } = new();

        public FakeSessionFactory Sessions { get; } = new();

        public FakeToolboxFactory Toolboxes { get; } = new();

        public FakeProfileStore Store { get; } = new();

        public FakeGitClient Git { get; } = new();

        public Task<ProfileBuildResult> BuildAsync(CancellationToken cancellationToken)
        {
            var builder = new ProfileBuilder(
                Providers,
                Sessions,
                Toolboxes,
                Store,
                Git,
                new NullProgressSink(Store),
                new StoppingBudgetPrompt(),
                TimeProvider.System,
                NullLogger<ProfileBuilder>.Instance);

            return builder.BuildAsync("/repo", null, cancellationToken);
        }
    }

    /// <summary>
    /// Answers every budget prompt with Stop, matching the hard-stop behaviour these tests
    /// exercised before continuing was an option. None of the sessions here ever hit a limit —
    /// they exist to answer the question if that assumption is ever wrong.
    /// </summary>
    private sealed class StoppingBudgetPrompt : IBudgetDecisionPrompt
    {
        public Task<BudgetDecision> AskAsync(LlmBudgetLimitReached limit, CancellationToken cancellationToken) =>
            Task.FromResult(BudgetDecision.Stop);
    }

    private sealed class NullProgressSink(FakeProfileStore store) : IToolProgressSink
    {
        public ValueTask ReportAsync(ToolProgressReport report, CancellationToken cancellationToken)
        {
            _ = store;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeProviderStore : IProviderProfileStore
    {
        public List<LlmProviderProfile> Profiles { get; } =
        [
            new()
            {
                Id = "p1",
                ProviderType = LlmProviderType.OpenAi,
                DisplayName = "Test provider",
                Model = "test-model",
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
                UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            },
        ];

        public string? ActiveId { get; set; } = "p1";

        public ValueTask<IReadOnlyList<LlmProviderProfile>> ListAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<LlmProviderProfile>>(Profiles);

        public ValueTask<LlmProviderProfile?> FindAsync(string id, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Profiles.FirstOrDefault(profile => profile.Id == id));

        public ValueTask SaveAsync(LlmProviderProfile profile, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DeleteAsync(string id, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<string?> GetActiveIdAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(ActiveId);

        public ValueTask SetActiveIdAsync(string? id, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeSessionFactory : ILlmSessionFactory
    {
        public FakeSession Session { get; private set; } = new();

        /// <summary>One answer per session created, in order. The second is the repair round.</summary>
        public IReadOnlyList<string> Answers { get; set; } = [Serialize(Sample.Document())];

        public int Created { get; private set; }

        public ValueTask<ILlmSession> CreateAsync(
            LlmProviderProfile profile,
            LlmBudget budget,
            CancellationToken cancellationToken)
        {
            var session = new FakeSession
            {
                StructuredJson = Answers.ElementAtOrDefault(Created) ?? Answers[^1],
            };

            Created++;

            // Only the first session's conversation is inspected: the repair round is a different
            // prompt, and asserting on it would pin the wording rather than the behaviour.
            if (Created == 1)
            {
                session.ThrowCancellation = Session.ThrowCancellation;
                session.Outcome = Session.Outcome;
                session.FailureCode = Session.FailureCode;
                Session = session;
            }

            return ValueTask.FromResult<ILlmSession>(session);
        }
    }

    private sealed class FakeSession : ILlmSession
    {
        public LlmConversation? Conversation { get; private set; }

        public string? StructuredJson { get; set; }

        public LlmRunOutcome Outcome { get; set; } = LlmRunOutcome.Completed;

        public string? FailureCode { get; set; }

        public bool ThrowCancellation { get; set; }

        public LlmUsage CumulativeUsage { get; } = new()
        {
            InputTokens = 1000,
            OutputTokens = 200,
            IsReported = true,
            EstimatedCostUsd = 0.01m,
        };

        public IReadOnlyList<LlmUsage> RequestUsages => [CumulativeUsage];

        public IReadOnlyList<LlmToolCallRecord> ToolCalls { get; } =
        [
            Call(1, "get_project_profile"),
            Call(2, "read_file"),
        ];

        public Task<LlmRunResult> RunAsync(
            LlmConversation conversation,
            IProgress<LlmRunEvent>? progress,
            CancellationToken cancellationToken,
            BudgetDecisionCallback? onBudgetExceeded = null)
        {
            Conversation = conversation;

            if (ThrowCancellation)
            {
                throw new OperationCanceledException();
            }

            return Task.FromResult(new LlmRunResult
            {
                Outcome = Outcome,
                StructuredJson = Outcome is LlmRunOutcome.Completed ? StructuredJson : null,
                FailureCode = FailureCode,
                Usage = CumulativeUsage,
                TurnCount = 3,
                ToolCalls = ToolCalls,
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static LlmToolCallRecord Call(int ordinal, string name) => new()
        {
            Ordinal = ordinal,
            Turn = 1,
            ToolName = name,
            ArgumentsPreview = "{}",
            ResultBytes = 42,
            Duration = TimeSpan.FromMilliseconds(5),
        };
    }

    private sealed class FakeToolboxFactory : IRepositoryToolboxFactory
    {
        public IReadOnlyList<string> ExcludedGlobs { get; private set; } = [];

        public async Task<IRepositoryToolbox> OpenAsync(
            string repositoryPath,
            IReadOnlyList<string> excludedGlobs,
            IToolProgressSink progress,
            CancellationToken cancellationToken)
        {
            ExcludedGlobs = excludedGlobs;

            // The recording sink the builder wrapped around ours is what puts the model's own
            // words into the stored trace, so a run has to actually push one through it.
            await progress
                .ReportAsync(new ToolProgressReport(1, "Reading the README", "exploring", DateTimeOffset.UnixEpoch), cancellationToken)
                .ConfigureAwait(false);

            return new Toolbox();
        }

        private sealed class Toolbox : IRepositoryToolbox
        {
            public IReadOnlyList<LlmToolDefinition> Tools => [];

            public int VisibleFileCount => 3;
        }
    }

    private sealed class FakeProfileStore : IProjectProfileStore
    {
        public ProjectProfile? Existing { get; set; }

        public ProjectProfileDocument? Saved { get; private set; }

        public ProfileProvenance? SavedProvenance { get; private set; }

        public bool UserSectionsSaved { get; private set; }

        public List<ProfileRun> Runs { get; } = [];

        public ValueTask<ProjectProfile?> GetAsync(string repositoryPath, CancellationToken cancellationToken)
        {
            if (Saved is null)
            {
                return ValueTask.FromResult(Existing);
            }

            var basis = Existing ?? ProjectProfile.Empty(repositoryPath, DateTimeOffset.UnixEpoch);
            return ValueTask.FromResult<ProjectProfile?>(basis with
            {
                Document = Saved,
                Provenance = SavedProvenance,
            });
        }

        public ValueTask SaveGeneratedAsync(
            string repositoryPath,
            ProjectProfileDocument document,
            ProfileProvenance provenance,
            CancellationToken cancellationToken)
        {
            Saved = document;
            SavedProvenance = provenance;
            return ValueTask.CompletedTask;
        }

        public ValueTask SaveEditedDocumentAsync(
            string repositoryPath,
            ProjectProfileDocument document,
            CancellationToken cancellationToken)
        {
            Saved = document;
            return ValueTask.CompletedTask;
        }

        public ValueTask SaveUserSectionsAsync(
            string repositoryPath,
            string userNotes,
            string customInstructions,
            IReadOnlyList<string> customExcludedGlobs,
            int? characterBudget,
            CancellationToken cancellationToken)
        {
            UserSectionsSaved = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(string repositoryPath, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask RecordRunAsync(ProfileRun run, CancellationToken cancellationToken)
        {
            Runs.Add(run);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<ProfileRun>> ListRunsAsync(
            string repositoryPath,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ProfileRun>>(Runs);
    }

    private sealed class FakeGitClient : IGitClient
    {
        public IReadOnlyList<string> VisibleFiles { get; set; } = ["README.md", "src/Program.cs"];

        public Task<string?> GetHeadCommitAsync(string repositoryPath, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("head0001");

        public Task<IReadOnlyList<string>> ListFilesAsync(
            FileListQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(VisibleFiles);

        public Task<CommitComparison> CompareWithHeadAsync(
            string repositoryPath,
            string commitSha,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CommitComparison(true, 0));

        public Task<Changeset> GetChangesetAsync(ChangesetQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Profiling describes the repository, not the changeset.");

        public Task<FileDiffResult> GetFileDiffAsync(FileDiffQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Profiling describes the repository, not the changeset.");

        public Task<FileContentResult> GetFileContentAsync(
            FileContentQuery query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The builder reads files through the toolbox.");

        public Task<GrepResult> GrepAsync(GrepQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The builder searches through the toolbox.");
    }
}
