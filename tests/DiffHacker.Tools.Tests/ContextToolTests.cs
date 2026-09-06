using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// <c>report_progress</c> and <c>get_project_profile</c>.
/// </summary>
public sealed class ContextToolTests
{
    [Fact]
    public async Task Progress_reaches_the_sink_with_its_message_and_phase()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        await toolbox.CallAsync("report_progress", new { message = "reading the auth changes", phase = "Exploring" });

        var report = toolbox.Progress.Reports.ShouldHaveSingleItem();
        report.Message.ShouldBe("reading the auth changes");
        report.Phase.ShouldBe("exploring");
        report.Sequence.ShouldBe(1);
        report.At.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Progress_sequence_numbers_increase_so_a_stale_one_can_be_dropped()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        await toolbox.CallAsync("report_progress", new { message = "first" });
        await toolbox.CallAsync("report_progress", new { message = "second" });
        await toolbox.CallAsync("report_progress", new { message = "third" });

        toolbox.Progress.Reports.Select(r => r.Sequence).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Progress_without_a_phase_is_fine()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        await toolbox.CallAsync("report_progress", new { message = "working" });

        toolbox.Progress.Reports.ShouldHaveSingleItem().Phase.ShouldBeNull();
    }

    [Fact]
    public async Task An_empty_progress_message_is_refused_without_reporting_anything()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("report_progress", new { message = "   " });

        result.ShouldContain("No message was given");
        toolbox.Progress.Reports.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sink_that_throws_does_not_fail_the_model_s_call()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        var catalogue = await Toolbox.OpenAsync(
            new ToolboxOptions
            {
                Git = GitClientFactory.Create(),
                LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
                Progress = new ThrowingProgressSink(),
            },
            repository.Root,
            TestContext.Current.CancellationToken);

        var tool = catalogue.LlmTools.Single(t => t.Name == "report_progress");
        var result = await tool.Invoke("""{"message":"still working"}""", TestContext.Current.CancellationToken);

        // Whether a notification reached a window is not something the model can act on, so
        // losing one must never cost it a turn.
        result.IsError.ShouldBeFalse();
        result.Content.ShouldContain("Recorded");
    }

    [Fact]
    public async Task The_profile_tool_says_there_is_none_and_what_to_do_instead()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var result = await toolbox.CallAsync("get_project_profile");

        result.ShouldContain("No profile has been stored");
        result.ShouldContain("get_repository_tree");
    }

    [Fact]
    public async Task The_profile_tool_returns_a_stored_profile_when_Iteration_6_supplies_one()
    {
        using var repository = FixtureRepository.CreateWithCommit();

        var catalogue = await Toolbox.OpenAsync(
            new ToolboxOptions
            {
                Git = GitClientFactory.Create(),
                LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
                Progress = new RecordingProgressSink(),
                Profiles = new StubProfileSource("This is a Rust workspace with three crates."),
            },
            repository.Root,
            TestContext.Current.CancellationToken);

        var tool = catalogue.LlmTools.Single(t => t.Name == "get_project_profile");
        var result = await tool.Invoke("{}", TestContext.Current.CancellationToken);

        result.Content.ShouldBe("This is a Rust workspace with three crates.");
    }

    private sealed class ThrowingProgressSink : Core.Tools.IToolProgressSink
    {
        public ValueTask ReportAsync(Core.Tools.ToolProgressReport report, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the bridge is gone");
    }

    private sealed class StubProfileSource(string profile) : IProjectProfileSource
    {
        public ValueTask<string?> GetAsync(string repositoryPath, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(profile);
    }
}
