using System.Text.Json;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Knowledge;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Settings;
using DiffHacker.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Core.Tests;

/// <summary>
/// The analysis orchestrator, tested against a fake session that honours the real session's
/// contract — it runs the caller's validator, hands rejections back until the cap is spent, and
/// then fails. Faking below that would test <c>DiffHacker.Llm</c> a second time; faking above it
/// would mean the repair loop was never exercised at all.
/// </summary>
public sealed class AnalysisRunnerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task With_no_provider_configured_nothing_is_run_and_nothing_is_stored()
    {
        var harness = new Harness();
        harness.Providers.Profiles.Clear();

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.FailureCode.ShouldBe(AnalysisFailures.NoProvider);
        harness.Sessions.Created.ShouldBe(0);
        harness.Store.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_clean_working_tree_is_refused_before_a_single_token_is_spent()
    {
        var harness = new Harness();
        harness.Git.Files = [];

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.FailureCode.ShouldBe(AnalysisFailures.CleanChangeset);
        harness.Sessions.Created.ShouldBe(0);
        harness.Toolboxes.Opened.ShouldBeFalse();
    }

    [Fact]
    public async Task A_completed_run_stores_the_document_with_its_provenance_and_trace()
    {
        var harness = new Harness();

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();

        var analysis = harness.Store.Saved.ShouldHaveSingleItem();

        analysis.HeadCommit.ShouldBe("head0001");
        analysis.Model.ShouldBe("gpt-4o");
        analysis.ProviderDisplayName.ShouldBe("Test");
        analysis.SchemaVersion.ShouldBe(Contracts.ContractVersion.Current);
        analysis.RepairRounds.ShouldBe(0);
        analysis.Document.Summary.ShouldContain("contract grew a field");

        // Requirement 7: the whole ordered trace, and the model's own progress messages beside it.
        analysis.ToolCalls.Select(static call => call.ToolName).ShouldBe(["get_project_profile", "get_file_diff"]);
        analysis.ProgressMessages.ShouldContain("Reading the contract");
    }

    [Fact]
    public async Task A_stored_run_records_a_content_hash_for_every_file_it_was_shown()
    {
        // The other half of the freshness check: without these, a later comparison could only say
        // whether line counts moved, and an edit that keeps them would go unseen.
        var harness = new Harness();

        await harness.RunAsync(TestContext.Current.CancellationToken);

        harness.Git.LastQuery.ShouldNotBeNull().HashContent.ShouldBeTrue();

        var analysis = harness.Store.Saved.ShouldHaveSingleItem();

        foreach (var file in analysis.ChangedFiles)
        {
            if (file.Status is ChangeStatus.Deleted)
            {
                file.ContentSha256.ShouldBeNull();
            }
            else
            {
                file.ContentSha256.ShouldBe("sha:" + file.Path);
            }
        }
    }

    [Fact]
    public async Task Every_changed_file_is_covered_by_the_stored_result()
    {
        // §0.2.5 asserted as set equality against the changeset the run was actually given, which
        // is the check verification step 2 asks for rather than a spot check.
        var harness = new Harness();

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        var covered = result.Analysis.ShouldNotBeNull().Document.Nodes
            .Select(static node => node.FilePath)
            .ToHashSet(StringComparer.Ordinal);

        covered.SetEquals(harness.Git.Files.Select(static file => file.Path)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_prompt_carries_the_file_list_and_no_file_contents()
    {
        // §0.2.9. The opening message names every path, and none of the text behind any of them.
        var harness = new Harness();

        await harness.RunAsync(TestContext.Current.CancellationToken);

        var opening = harness.Sessions.Session.Conversation.ShouldNotBeNull().UserMessage;

        opening.ShouldContain(AnalysisFixtures.ContractPath);
        opening.ShouldContain(AnalysisFixtures.IconPath);
        opening.ShouldNotContain(Harness.FileContents);
        opening.ShouldNotContain("@@");
        opening.ShouldNotContain("+++ b/");
    }

    [Fact]
    public async Task The_prompt_carries_the_stored_profile_and_the_reviewers_instructions()
    {
        var harness = new Harness();

        await harness.RunAsync(TestContext.Current.CancellationToken);

        var opening = harness.Sessions.Session.Conversation.ShouldNotBeNull().UserMessage;

        opening.ShouldContain("A desktop reviewer for large diffs.");
        opening.ShouldContain("Ignore the generated folder.");
    }

    [Fact]
    public async Task The_conversation_asks_for_the_analysis_schema_from_the_contract_set()
    {
        var harness = new Harness();

        await harness.RunAsync(TestContext.Current.CancellationToken);

        var conversation = harness.Sessions.Session.Conversation.ShouldNotBeNull();
        var format = conversation.ResponseFormat.ShouldNotBeNull();

        format.SchemaName.ShouldBe("analysis_result");
        format.SchemaJson.ShouldContain("\"title\": \"AnalysisResult\"");

        // The fixture changeset is 3 files, well under the threshold where more rounds are
        // granted, so this pins the floor rather than the scaling itself.
        conversation.MaxResultRepairs.ShouldBe(2);
        conversation.MaxSchemaRepairs.ShouldBe(1);
        conversation.ResultValidator.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_run_that_wants_both_groupings_stores_both_of_them()
    {
        var harness = new Harness();

        await harness.RunAsync(TestContext.Current.CancellationToken);

        var stored = harness.Store.Saved.ShouldHaveSingleItem();

        stored.AvailableGroupings.ShouldBe(
            [AnalysisGrouping.DependencyFlow, AnalysisGrouping.ChangeClusters]);

        // And the same nodes in each: Iteration 11's verification step 1, asserted where the answer
        // is stored rather than where it is drawn.
        var nodes = stored.Document.Nodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var grouping in stored.AvailableGroupings)
        {
            stored.Document.For(grouping).Containers
                .SelectMany(static container => container.NodeIds)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(nodes)
                .ShouldBeTrue();
        }
    }

    [Fact]
    public async Task A_run_that_wants_one_grouping_asks_for_the_smaller_prompt_and_the_smaller_schema()
    {
        // Both halves, because either one alone is a contradiction: a schema with a field the prompt
        // never mentions, or a prompt asking for a field the schema forbids.
        var harness = new Harness { Options = new AnalysisRunOptions { ChangeClusters = false } };
        harness.Sessions.Answers = [Serialize(AnalysisFixtures.DependencyOnly())];

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();

        var conversation = harness.Sessions.Session.Conversation.ShouldNotBeNull();

        conversation.SystemPrompt.ShouldNotContain("clusterContainers");
        conversation.ResponseFormat.ShouldNotBeNull().SchemaJson.ShouldNotContain("clusterContainers");

        // And the answer is accepted rather than sent back for a grouping nobody asked for.
        harness.Sessions.Session.Rejections.ShouldBeEmpty();
        harness.Store.Saved.ShouldHaveSingleItem()
            .AvailableGroupings.ShouldBe([AnalysisGrouping.DependencyFlow]);
    }

    [Fact]
    public async Task A_run_that_does_not_want_implementation_groups_never_asks_and_stores_none()
    {
        var harness = new Harness { Options = new AnalysisRunOptions { ImplementationGroups = false } };
        harness.Sessions.Answers = [Serialize(AnalysisFixtures.Valid() with { ImplementationGroups = null })];

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();

        var conversation = harness.Sessions.Session.Conversation.ShouldNotBeNull();

        conversation.SystemPrompt.ShouldNotContain("implementationGroups");
        conversation.ResponseFormat.ShouldNotBeNull().SchemaJson.ShouldNotContain("implementationGroups");

        // Null rather than empty is how the stored analysis says "nobody asked".
        harness.Store.Saved.ShouldHaveSingleItem().Document.ImplementationGroups.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_that_wanted_implementation_groups_rejects_an_answer_without_them()
    {
        var harness = new Harness();

        harness.Sessions.Answers =
        [
            Serialize(AnalysisFixtures.Valid() with { ImplementationGroups = null }),
            Serialize(AnalysisFixtures.WithImplementationGroup()),
        ];

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();

        harness.Sessions.Session.Rejections.ShouldHaveSingleItem()
            .ShouldContain(message => message.Contains("implementationGroups is missing"));

        harness.Store.Saved.ShouldHaveSingleItem().Document.ImplementationGroups
            .ShouldNotBeNull().ShouldHaveSingleItem().AbstractionNodeId.ShouldBe(AnalysisFixtures.ContractPath);
    }

    [Fact]
    public async Task A_run_that_wanted_both_groupings_rejects_an_answer_with_one()
    {
        var harness = new Harness();

        harness.Sessions.Answers =
            [Serialize(AnalysisFixtures.DependencyOnly()), Serialize(AnalysisFixtures.Valid())];

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();

        harness.Sessions.Session.Rejections.ShouldHaveSingleItem()
            .ShouldContain(message => message.Contains("change-clusters grouping is empty"));
    }

    [Fact]
    public async Task The_statistics_stored_describe_the_grouping_an_analysis_opens_in()
    {
        // Three container counts are in play — two groupings and the changeset — and the stored
        // record is the dependency-flow one, because that is the picture an analysis opens on.
        var harness = new Harness();

        await harness.RunAsync(TestContext.Current.CancellationToken);

        var stored = harness.Store.Saved.ShouldHaveSingleItem();

        stored.Statistics.ContainerCount.ShouldBe(2);
        stored.StatisticsFor(AnalysisGrouping.DependencyFlow).ContainerCount.ShouldBe(2);
        stored.StatisticsFor(AnalysisGrouping.ChangeClusters).ContainerCount.ShouldBe(3);

        // The numbers that are not about grouping do not move.
        stored.StatisticsFor(AnalysisGrouping.ChangeClusters).NodeCount
            .ShouldBe(stored.Statistics.NodeCount);
    }

    [Fact]
    public async Task Repair_rounds_scale_up_with_how_many_files_changed()
    {
        // Every round re-emits the whole document at the model's output rate, so a fixed count
        // either wastes rounds on a small change or runs out on a large one before a first pass's
        // many small, independent mistakes are all fixed.
        var harness = new Harness();
        harness.Git.Files = [.. Enumerable.Range(0, 350).Select(i => AnalysisFixtures.File($"src/File{i}.cs"))];

        await harness.RunAsync(TestContext.Current.CancellationToken);

        var conversation = harness.Sessions.Session.Conversation.ShouldNotBeNull();
        conversation.MaxResultRepairs.ShouldBe(5, "350 files: floor 2 plus 3 for the extra 300.");
        conversation.MaxSchemaRepairs.ShouldBe(4, "350 files: floor 1 plus 3 for the extra 300.");
    }

    [Fact]
    public async Task A_rejected_result_is_repaired_and_the_rounds_are_recorded()
    {
        var harness = new Harness();

        // First answer drops the icon; the second covers everything.
        harness.Sessions.Answers = [Serialize(WithoutTheIcon()), Serialize(AnalysisFixtures.Valid())];

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        harness.Store.Saved.ShouldHaveSingleItem().RepairRounds.ShouldBe(1);

        // And what went back named the file that was missing, rather than saying "try again".
        harness.Sessions.Session.Rejections.ShouldHaveSingleItem()
            .ShouldContain(message => message.Contains(AnalysisFixtures.IconPath));
    }

    [Fact]
    public async Task When_the_repairs_are_exhausted_the_run_fails_and_stores_nothing()
    {
        // Verification step 5: fail loudly, show what was wrong, and persist nothing that could
        // later be mistaken for a valid result.
        var harness = new Harness();
        harness.Sessions.Answers = [Serialize(WithoutTheIcon())];

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.FailureCode.ShouldBe(LlmFailures.ResultRejected);
        result.Diagnostics.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.FileNotCovered);
        result.Diagnostics.ShouldAllBe(d => d.Severity == AnalysisDiagnosticSeverity.Error);
        harness.Store.Saved.ShouldBeEmpty();

        // The attempt still cost money and still says so.
        result.Usage.TotalTokens.ShouldBe(1200);
    }

    [Fact]
    public async Task A_cancelled_run_reports_what_it_spent_and_stores_nothing()
    {
        var harness = new Harness();
        harness.Sessions.Session.ThrowCancellation = true;

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await harness.RunAsync(TestContext.Current.CancellationToken));

        harness.Store.Saved.ShouldBeEmpty();
        harness.Sessions.Session.CumulativeUsage.TotalTokens.ShouldBe(1200);
    }

    [Fact]
    public async Task A_context_overflow_comes_back_as_its_own_code_rather_than_a_generic_failure()
    {
        // The fixed decision in the iteration: an overflowing changeset is surfaced as an
        // actionable error, never as a silently truncated file list.
        var harness = new Harness();
        harness.Sessions.Session.Outcome = LlmRunOutcome.ContextOverflow;
        harness.Sessions.Session.FailureCode = LlmFailures.ContextOverflow;

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.FailureCode.ShouldBe(LlmFailures.ContextOverflow);
        harness.Store.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Warnings_travel_with_the_stored_result_and_errors_never_do()
    {
        var harness = new Harness();
        var withCycle = AnalysisFixtures.Valid();

        harness.Sessions.Answers =
        [
            Serialize(withCycle with
            {
                Edges =
                [
                    withCycle.Edges[0],
                    new AnalysisEdge
                    {
                        SourceNodeId = AnalysisFixtures.CallerPath,
                        TargetNodeId = AnalysisFixtures.ContractPath,
                        Kind = AnalysisEdgeKind.Conceptual,
                        Explanation = "And back again.",
                        Risks = [],
                    },
                ],
            }),
        ];

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();

        var stored = harness.Store.Saved.ShouldHaveSingleItem();

        stored.Diagnostics.ShouldContain(d => d.Code == AnalysisDiagnosticCodes.Cycle);
        stored.Diagnostics.ShouldAllBe(d => d.Severity == AnalysisDiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Statistics_are_computed_by_the_application_and_not_taken_from_the_answer()
    {
        var harness = new Harness();

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);
        var statistics = result.Analysis.ShouldNotBeNull().Statistics;

        statistics.Changeset.TotalFiles.ShouldBe(3);
        statistics.NodeCount.ShouldBe(3);
        statistics.ContainerCount.ShouldBe(2);
        statistics.EdgeCount.ShouldBe(1);
        statistics.DirectEdgeCount.ShouldBe(1);
        statistics.ConceptualEdgeCount.ShouldBe(0);
        statistics.LongestChain.ShouldBe(2);
    }

    [Fact]
    public async Task Two_runs_of_the_same_changeset_produce_the_same_node_ids()
    {
        // Verification step 9. Ids are path-derived by contract, so this holds for reasons the
        // model cannot undo rather than because it happened to answer the same way twice.
        var first = await new Harness().RunAsync(TestContext.Current.CancellationToken);
        var second = await new Harness().RunAsync(TestContext.Current.CancellationToken);

        Ids(first).ShouldBe(Ids(second));
        Ids(first).ShouldBe([AnalysisFixtures.IconPath, AnalysisFixtures.CallerPath, AnalysisFixtures.ContractPath]);

        static string[] Ids(AnalysisRunResult run) =>
        [
            .. run.Analysis!.Document.Nodes
                .Select(static node => node.Id)
                .Order(StringComparer.Ordinal),
        ];
    }

    [Fact]
    public async Task Risks_stay_in_their_own_fields_and_are_never_folded_into_the_prose()
    {
        // Verification step 10. Nothing merges them on the way in, and the stored document is the
        // model's own, so the only way this could break is if something rewrote it in between.
        var harness = new Harness();

        var result = await harness.RunAsync(TestContext.Current.CancellationToken);
        var document = result.Analysis.ShouldNotBeNull().Document;

        document.OverallRisks.ShouldHaveSingleItem().ShouldContain("icon was removed");
        document.Summary.ShouldNotContain("icon was removed");

        foreach (var node in document.Nodes)
        {
            foreach (var risk in node.Risks)
            {
                node.WhatChanged.ShouldNotContain(risk);
                node.ImplementationNotes.ShouldNotContain(risk);
            }
        }
    }

    private static AnalysisResult WithoutTheIcon()
    {
        var result = AnalysisFixtures.Valid();

        return result with
        {
            Nodes = [.. result.Nodes.Where(static node => node.FilePath != AnalysisFixtures.IconPath)],
            DependencyContainers =
                [.. result.DependencyContainers.Where(static container => container.Id != "removed-assets")],
            DependencyReadingOrder = [AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath],
            ClusterContainers = [.. result.ClusterContainers.Where(static container => container.Id != "assets")],
            ClusterReadingOrder = [AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath],
        };
    }

    private static string Serialize(AnalysisResult result) => JsonSerializer.Serialize(result, Json);

    private sealed class Harness
    {
        /// <summary>Text that exists in the repository and must never reach the prompt.</summary>
        public const string FileContents = "public sealed record Contract(string Tenant);";

        public FakeProviderStore Providers { get; } = new();

        public FakeSessionFactory Sessions { get; } = new();

        public FakeToolboxFactory Toolboxes { get; } = new();

        public FakeProfileStore Profiles { get; } = new();

        public FakeAnalysisStore Store { get; } = new();

        public FakeGitClient Git { get; } = new();

        /// <summary>What the run asks the model for. Both groupings unless a test says otherwise.</summary>
        public AnalysisRunOptions Options { get; set; } = AnalysisRunOptions.Default;

        public Task<AnalysisRunResult> RunAsync(CancellationToken cancellationToken)
        {
            var runner = new AnalysisRunner(
                Providers,
                Sessions,
                Toolboxes,
                Profiles,
                Store,
                Git,
                new NullProgressSink(),
                TimeProvider.System,
                NullLogger<AnalysisRunner>.Instance);

            return runner.RunAsync("/repo", Options, null, cancellationToken);
        }
    }

    private sealed class NullProgressSink : IToolProgressSink
    {
        public ValueTask ReportAsync(ToolProgressReport report, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeProviderStore : IProviderProfileStore
    {
        public List<LlmProviderProfile> Profiles { get; } =
        [
            new()
            {
                Id = "p1",
                ProviderType = LlmProviderType.OpenAi,
                DisplayName = "Test",
                Model = "gpt-4o",
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
                UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            },
        ];

        public string? ActiveId { get; set; } = "p1";

        public ValueTask<IReadOnlyList<LlmProviderProfile>> ListAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<LlmProviderProfile>>(Profiles);

        public ValueTask<LlmProviderProfile?> FindAsync(string id, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Profiles.Find(profile => profile.Id == id));

        public ValueTask<string?> GetActiveIdAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(ActiveId);

        public ValueTask SaveAsync(LlmProviderProfile profile, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A run never writes a provider profile.");

        public ValueTask DeleteAsync(string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A run never deletes a provider profile.");

        public ValueTask SetActiveIdAsync(string? id, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A run never changes which provider is active.");
    }

    /// <summary>
    /// A session that honours <see cref="ILlmSession"/>'s contract around the validation hook:
    /// each scripted answer is checked, a rejection consumes a repair round, and running out of
    /// rounds fails with the answer that was refused.
    /// </summary>
    private sealed class FakeSessionFactory : ILlmSessionFactory
    {
        public FakeSession Session { get; private set; } = new();

        public IReadOnlyList<string> Answers { get; set; } = [Serialize(AnalysisFixtures.Valid())];

        public int Created { get; private set; }

        public ValueTask<ILlmSession> CreateAsync(
            LlmProviderProfile profile,
            LlmBudget budget,
            CancellationToken cancellationToken)
        {
            Created++;
            Session = new FakeSession
            {
                Answers = Answers,
                ThrowCancellation = Session.ThrowCancellation,
                Outcome = Session.Outcome,
                FailureCode = Session.FailureCode,
            };

            return ValueTask.FromResult<ILlmSession>(Session);
        }
    }

    private sealed class FakeSession : ILlmSession
    {
        public LlmConversation? Conversation { get; private set; }

        public IReadOnlyList<string> Answers { get; init; } = [];

        /// <summary>Every set of failures handed back, in order.</summary>
        public List<IReadOnlyList<string>> Rejections { get; } = [];

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
            Call(2, "get_file_diff"),
        ];

        public Task<LlmRunResult> RunAsync(
            LlmConversation conversation,
            IProgress<LlmRunEvent>? progress,
            CancellationToken cancellationToken)
        {
            Conversation = conversation;

            if (ThrowCancellation)
            {
                throw new OperationCanceledException();
            }

            if (Outcome is not LlmRunOutcome.Completed)
            {
                return Task.FromResult(Result(null, 0));
            }

            for (var attempt = 0; ; attempt++)
            {
                // A script that runs out repeats its last answer, the same way the end-to-end stub
                // provider does. It is what makes "always answers badly" one entry rather than one
                // per repair round the cap happens to allow.
                var answer = Answers[Math.Min(attempt, Answers.Count - 1)];
                var rejections = conversation.ResultValidator?.Invoke(answer) ?? [];

                if (rejections.Count == 0)
                {
                    return Task.FromResult(Result(answer, attempt));
                }

                Rejections.Add(rejections);

                if (Rejections.Count > conversation.MaxResultRepairs)
                {
                    return Task.FromResult(new LlmRunResult
                    {
                        Outcome = LlmRunOutcome.Failed,
                        FailureCode = LlmFailures.ResultRejected,
                        ProviderMessage = string.Join("; ", rejections),
                        StructuredJson = answer,
                        Usage = CumulativeUsage,
                        TurnCount = attempt + 1,
                        ToolCalls = ToolCalls,
                        ResultRepairs = conversation.MaxResultRepairs,
                    });
                }
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private LlmRunResult Result(string? answer, int attempt) => new()
        {
            Outcome = Outcome,
            StructuredJson = Outcome is LlmRunOutcome.Completed ? answer : null,
            FailureCode = FailureCode,
            Usage = CumulativeUsage,
            TurnCount = attempt + 1,
            ToolCalls = ToolCalls,
            ResultRepairs = attempt,
        };

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
        public bool Opened { get; private set; }

        public async Task<IRepositoryToolbox> OpenAsync(
            string repositoryPath,
            IReadOnlyList<string> excludedGlobs,
            IToolProgressSink progress,
            CancellationToken cancellationToken)
        {
            Opened = true;

            // The recording sink the runner wrapped around ours is what puts the model's own words
            // into the stored trace, so a run has to actually push one through it.
            await progress
                .ReportAsync(
                    new ToolProgressReport(1, "Reading the contract", "exploring", DateTimeOffset.UnixEpoch),
                    cancellationToken)
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
        public ValueTask<ProjectProfile?> GetAsync(string repositoryPath, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProjectProfile?>(new ProjectProfile
            {
                RepositoryPath = repositoryPath,
                CustomInstructions = "Ignore the generated folder.",
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
                UpdatedAtUtc = DateTimeOffset.UnixEpoch,
                Document = new ProjectProfileDocument
                {
                    Purpose = "A desktop reviewer for large diffs.",
                    Architecture = "A host and a renderer.",
                    Layering = "Core knows nothing of the host.",
                    Patterns = "Contracts are generated.",
                    TestLayout = "xUnit beside each project.",
                },
            });

        public ValueTask SaveGeneratedAsync(
            string repositoryPath,
            ProjectProfileDocument document,
            ProfileProvenance provenance,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("An analysis reads the profile and never writes it.");

        public ValueTask SaveEditedDocumentAsync(
            string repositoryPath,
            ProjectProfileDocument document,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("An analysis reads the profile and never writes it.");

        public ValueTask SaveUserSectionsAsync(
            string repositoryPath,
            string userNotes,
            string customInstructions,
            IReadOnlyList<string> customExcludedGlobs,
            int? characterBudget,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("An analysis reads the profile and never writes it.");

        public ValueTask DeleteAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("An analysis never deletes the profile.");

        public ValueTask RecordRunAsync(ProfileRun run, CancellationToken cancellationToken) =>
            throw new NotSupportedException("An analysis records its own run, not a profile run.");

        public ValueTask<IReadOnlyList<ProfileRun>> ListRunsAsync(
            string repositoryPath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("An analysis does not read profile runs.");
    }

    private sealed class FakeAnalysisStore : IAnalysisStore
    {
        public List<Analysis> Saved { get; } = [];

        public ValueTask SaveAsync(Analysis analysis, CancellationToken cancellationToken)
        {
            Saved.Add(analysis);
            return ValueTask.CompletedTask;
        }

        public ValueTask<Analysis?> GetLatestAsync(string repositoryPath, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Saved.LastOrDefault());

        public ValueTask<Analysis?> FindAsync(string analysisId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Saved.Find(analysis => analysis.Id == analysisId));

        public ValueTask<IReadOnlyList<Analysis>> ListAsync(
            string repositoryPath,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<Analysis>>(Saved);

        public ValueTask<IReadOnlyList<AnalysisSummary>> ListSummariesAsync(
            string repositoryPath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("A run does not read the library.");

        public ValueTask DeleteAsync(string repositoryPath, CancellationToken cancellationToken)
        {
            Saved.Clear();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteOneAsync(string analysisId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A run does not delete an analysis.");

        public ValueTask<IReadOnlyList<string>> SetNodesReviewedAsync(
            string analysisId,
            IReadOnlyList<string> nodeIds,
            bool reviewed,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("A run does not mark anything reviewed.");

        public ValueTask SetGroupingAsync(
            string analysisId,
            AnalysisGrouping grouping,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("A run does not choose which grouping to read it in.");
    }

    private sealed class FakeGitClient : IGitClient
    {
        public IReadOnlyList<ChangedFile> Files { get; set; } = AnalysisFixtures.Changeset();

        public ChangesetQuery? LastQuery { get; private set; }

        public Task<Changeset> GetChangesetAsync(ChangesetQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;

            // Stands in for the real hashing: what matters here is that the runner asked for it and
            // kept what came back, not what SHA-256 of a fixture is.
            IReadOnlyList<ChangedFile> files = query.HashContent
                ? [.. Files.Select(static file => file with
                {
                    ContentSha256 = file.Status is ChangeStatus.Deleted ? null : "sha:" + file.Path,
                })]
                : Files;

            return Task.FromResult(new Changeset
            {
                RepositoryPath = query.RepositoryPath,
                IsClean = files.Count == 0,
                HasCommits = true,
                UntrackedIncluded = true,
                Files = files,
                Statistics = ChangesetStatistics.From(files),
                HunkCountsAvailable = true,
            });
        }

        public Task<string?> GetHeadCommitAsync(string repositoryPath, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("head0001");

        public Task<IReadOnlyList<string>> ListFilesAsync(
            FileListQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([AnalysisFixtures.ContractPath, AnalysisFixtures.CallerPath]);

        public Task<CommitComparison> CompareWithHeadAsync(
            string repositoryPath,
            string commitSha,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("An analysis does not measure profile drift.");

        public Task<FileDiffResult> GetFileDiffAsync(FileDiffQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The runner reads diffs through the toolbox.");

        public Task<FileContentResult> GetFileContentAsync(
            FileContentQuery query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The runner reads files through the toolbox.");

        public Task<GrepResult> GrepAsync(GrepQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The runner searches through the toolbox.");
    }
}
