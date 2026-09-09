using DiffHacker.Contracts;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Llm;
using DiffHacker.Host.Rpc;
using DiffHacker.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The analysis RPC surface against a real database, with only the run itself faked — there is no
/// test here that reaches a provider.
/// <para>
/// The assertion that matters most is the last one: reopening a stored analysis must not start a
/// conversation. It is the whole reason the result is persisted, and the only way to prove it is to
/// count the runs the stub was asked for.
/// </para>
/// </summary>
public sealed class AnalysisRpcTests : IAsyncLifetime
{
    private readonly DirectoryInfo _dataDirectory =
        Directory.CreateTempSubdirectory("diffhacker-analysis-rpc-");

    private AppDatabase _database = null!;
    private SqliteAnalysisStore _store = null!;
    private StubAnalysisRunner _runner = null!;
    private AnalysisRpcTarget _target = null!;

    public ValueTask InitializeAsync()
    {
        _database = new AppDatabase(
            Path.Combine(_dataDirectory.FullName, "diffhacker.db"),
            NullLogger<AppDatabase>.Instance);

        _store = new SqliteAnalysisStore(_database);
        _runner = new StubAnalysisRunner(_store);
        _target = Target();

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        try
        {
            _dataDirectory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A file handle outliving the test is not a test failure.
        }
    }

    [Fact]
    public async Task A_repository_with_no_analysis_reports_that_rather_than_failing()
    {
        var view = await _target.GetAsync(Request(), TestContext.Current.CancellationToken);

        view.HasAnalysis.ShouldBeFalse();
        view.RepositoryPath.ShouldBe("/repo");
        view.Nodes.ShouldBeEmpty();
        view.Containers.ShouldBeEmpty();
        view.ChangedFiles.ShouldBeEmpty();
        view.AnalysisId.ShouldBeNull();
    }

    [Fact]
    public async Task The_changeset_the_run_was_made_from_travels_with_the_graph()
    {
        // Iteration 8 requirement 2: the node boxes draw line counts, a status word and a project,
        // and none of the three is in the model's answer. They are recorded at run time and stored,
        // never re-read — joining a stored analysis against today's working tree would print
        // today's numbers on a diagram of yesterday's change.
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        view.ChangedFiles.Select(static file => file.Path)
            .ShouldBe(["src/Contract.cs", "src/Caller.cs", "assets/icon.png"]);

        var contract = view.ChangedFiles[0];
        contract.Status.ShouldBe(ChangedFileFactsInfoStatus.Modified);
        contract.LinesAdded.ShouldBe(12);
        contract.LinesRemoved.ShouldBe(3);
        contract.Project.ShouldBe("DiffHacker");
        contract.Language.ShouldBe("C#");
    }

    [Fact]
    public async Task A_binary_file_reaches_the_renderer_with_no_line_counts_rather_than_zero()
    {
        // "We did not count" and "we counted nothing" are different claims. A box reading +0 −0
        // makes the wrong one, so the absence has to survive every hop to the screen.
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var icon = view.ChangedFiles.Single(static file => file.Path == "assets/icon.png");

        icon.IsBinary.ShouldBeTrue();
        icon.LinesAdded.ShouldBeNull();
        icon.LinesRemoved.ShouldBeNull();
        icon.Language.ShouldBeNull();
        icon.Status.ShouldBe(ChangedFileFactsInfoStatus.Deleted);
    }

    [Fact]
    public async Task A_completed_run_comes_back_as_the_whole_view()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        view.HasAnalysis.ShouldBeTrue();
        view.Summary.ShouldBe("The contract grew a field.");
        view.Model.ShouldBe("gpt-4o");
        view.RepairRounds.ShouldBe(1);
        view.CostUsd.ShouldBe("0.25");
        view.Containers.ShouldHaveSingleItem().EntryNodeId.ShouldBe("src/Contract.cs");
        view.OverallRisks.ShouldHaveSingleItem();
        view.Statistics.ShouldNotBeNull().NodeCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_view_resolves_the_container_each_node_is_in_and_orders_them_for_reading()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        view.Nodes.Select(static node => node.Id).ShouldBe(["src/Contract.cs", "src/Caller.cs"]);
        view.Nodes.ShouldAllBe(node => node.ContainerId == "core");
        view.Nodes[0].Rank.ShouldBe(1);
        view.Nodes[0].States.ShouldContain(AnalysisNodeInfoState.Entry_point);
        view.Nodes[1].States.ShouldContain(AnalysisNodeInfoState.Unchanged_relevant);
    }

    [Fact]
    public async Task An_edge_inside_one_container_is_not_marked_as_crossing_one()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var edge = view.Edges.ShouldHaveSingleItem();

        edge.CrossesContainers.ShouldBeFalse();
        edge.Kind.ShouldBe(AnalysisEdgeInfoKind.Conceptual);
    }

    [Fact]
    public async Task Warnings_reach_the_renderer_so_a_cycle_is_visible_rather_than_silent()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var diagnostic = view.Diagnostics.ShouldHaveSingleItem();

        diagnostic.Severity.ShouldBe(AnalysisDiagnosticInfoSeverity.Warning);
        diagnostic.Code.ShouldBe(AnalysisDiagnosticCodes.Cycle);
        diagnostic.Subject.ShouldBe("src/Caller.cs");
    }

    [Fact]
    public async Task A_validation_failure_carries_the_specific_diagnostics_into_the_error()
    {
        // Requirement 4: the run fails loudly and the user is shown what was wrong. A code with no
        // detail would be the quiet failure that requirement exists to forbid.
        _runner.FailWith = AnalysisFailures.ValidationFailed;

        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.RunAsync(Request(), TestContext.Current.CancellationToken));

        var data = failure.ErrorData.ShouldBeOfType<RpcErrorData>();

        data.Code.ShouldBe(AnalysisFailures.ValidationFailed);
        data.Args.ShouldNotBeNull()["detail"].ShouldContain("No node covers the changed file 'src/Dropped.cs'.");
    }

    [Fact]
    public async Task A_missing_provider_comes_back_as_its_own_code()
    {
        _runner.FailWith = AnalysisFailures.NoProvider;

        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.RunAsync(Request(), TestContext.Current.CancellationToken));

        failure.ErrorData.ShouldBeOfType<RpcErrorData>().Code.ShouldBe(AnalysisFailures.NoProvider);
    }

    [Fact]
    public async Task A_provider_that_is_not_configured_properly_surfaces_its_configuration_code()
    {
        _runner.Throw = new LlmConfigurationException("provider_key_missing", "No key is stored.");

        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.RunAsync(Request(), TestContext.Current.CancellationToken));

        failure.ErrorData.ShouldBeOfType<RpcErrorData>().Code.ShouldBe("provider_key_missing");
    }

    [Fact]
    public async Task A_failed_run_stores_nothing_that_a_later_get_could_mistake_for_a_result()
    {
        _runner.FailWith = AnalysisFailures.ValidationFailed;

        await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.RunAsync(Request(), TestContext.Current.CancellationToken));

        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken)).HasAnalysis.ShouldBeFalse();
    }

    [Fact]
    public async Task Reopening_a_stored_analysis_never_runs_the_model_again()
    {
        // Verification step 7, asserted from the only log that could prove it: how many times the
        // runner was asked to run at all.
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        _runner.Runs.ShouldBe(1);

        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        _database = new AppDatabase(
            Path.Combine(_dataDirectory.FullName, "diffhacker.db"),
            NullLogger<AppDatabase>.Instance);

        _store = new SqliteAnalysisStore(_database);
        var runner = new StubAnalysisRunner(_store);
        var restarted = new AnalysisRpcTarget(
            _store,
            runner,
            new RunEventNotifier(new SilentNotifier(), NullLogger<RunEventNotifier>.Instance),
            NullLogger<AnalysisRpcTarget>.Instance);

        var view = await restarted.GetAsync(Request(), TestContext.Current.CancellationToken);

        view.HasAnalysis.ShouldBeTrue();
        view.Nodes.Count.ShouldBe(2);
        runner.Runs.ShouldBe(0);
    }

    private AnalysisRpcTarget Target() => new(
        _store,
        _runner,
        new RunEventNotifier(new SilentNotifier(), NullLogger<RunEventNotifier>.Instance),
        NullLogger<AnalysisRpcTarget>.Instance);

    private static AnalysisRequest Request() => new(repositoryPath: "/repo");

    /// <summary>
    /// Stands in for the whole pipeline. The orchestration itself is covered by
    /// <c>AnalysisRunnerTests</c>; what is being tested here is the surface above it.
    /// </summary>
    private sealed class StubAnalysisRunner(SqliteAnalysisStore store) : IAnalysisRunner
    {
        public int Runs { get; private set; }

        public string? FailWith { get; set; }

        public Exception? Throw { get; set; }

        public async Task<AnalysisRunResult> RunAsync(
            string repositoryPath,
            IProgress<LlmRunEvent>? progress,
            CancellationToken cancellationToken)
        {
            Runs++;

            if (Throw is { } thrown)
            {
                throw thrown;
            }

            if (FailWith is { } code)
            {
                return new AnalysisRunResult
                {
                    FailureCode = code,
                    Diagnostics = code == AnalysisFailures.ValidationFailed
                        ?
                        [
                            AnalysisDiagnostic.Error(
                                AnalysisDiagnosticCodes.FileNotCovered,
                                "src/Dropped.cs",
                                "No node covers the changed file 'src/Dropped.cs'."),
                        ]
                        : [],
                };
            }

            var analysis = AnalysisSamples.Completed(repositoryPath);
            await store.SaveAsync(analysis, cancellationToken);

            return new AnalysisRunResult { Analysis = analysis, Usage = analysis.Usage };
        }
    }

    private sealed class SilentNotifier : IRpcNotifier
    {
        public Task NotifyAsync(string method, object payload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
