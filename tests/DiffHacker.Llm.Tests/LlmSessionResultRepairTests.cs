using DiffHacker.Core.Llm;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// The caller-supplied validation hook, which is where Iteration 7's repair loop lives.
/// <para>
/// The point of putting it inside the run rather than after it is that the model keeps its context
/// and its tools, so a rejection is something it can go and fix properly. These tests assert that
/// specifically: the repair message carries the diagnostics verbatim, and the tools are still bound
/// on the turn that follows it.
/// </para>
/// </summary>
public sealed class LlmSessionResultRepairTests
{
    private const string Good = """{"summary":"ok","confidence":4}""";
    private const string Bad = """{"summary":"","confidence":1}""";

    [Fact]
    public async Task Without_a_validator_an_answer_that_matches_the_schema_is_accepted()
    {
        var harness = new SessionHarness();
        harness.Provider.Says(Good);

        await using var session = harness.Build();

        var run = await session.RunAsync(
            SessionHarness.Conversation(format: SessionHarness.AnswerFormat),
            null,
            TestContext.Current.CancellationToken);

        run.Succeeded.ShouldBeTrue();
        run.ResultRepairs.ShouldBe(0);
    }

    [Fact]
    public async Task A_rejected_result_is_handed_back_with_the_specific_failures_named()
    {
        // Verification 4 of the iteration: inspect the repair message, not merely that a retry
        // happened. A generic "try again" would cost the same money and fix nothing.
        var harness = new SessionHarness();
        harness.Provider.Says(Bad).Says(Good);

        await using var session = harness.Build();

        var run = await session.RunAsync(
            Conversation(harness, maxRepairs: 2),
            null,
            TestContext.Current.CancellationToken);

        run.Succeeded.ShouldBeTrue();
        run.ResultRepairs.ShouldBe(1);

        var repair = harness.Provider.Requests[1].Messages[^1];

        repair.Text.ShouldContain("no node covers 'src/Cache.cs'");
        repair.Text.ShouldContain("container 'core' has two entry nodes");

        // And it tells the model the two things it must not do to make the check pass, because
        // deleting the offending node would satisfy every rule this validator can express.
        repair.Text.ShouldContain("Do not delete anything");
        repair.Text.ShouldContain("do not invent anything");
    }

    [Fact]
    public async Task The_tools_are_still_available_on_the_turn_after_a_rejection()
    {
        // The whole reason the check lives inside the loop. A model told "no node covers X" can
        // only answer honestly by going and looking at X.
        var harness = new SessionHarness();
        harness.Provider.Says(Bad).Says(Good);

        await using var session = harness.Build();

        await session.RunAsync(
            Conversation(harness, maxRepairs: 1, tools: [SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken);

        harness.Provider.Requests[1].ToolNames.ShouldContain("echo");
    }

    [Fact]
    public async Task The_conversation_is_kept_rather_than_restarted_so_the_exploration_survives()
    {
        var harness = new SessionHarness();
        harness.Provider.Says(Bad).Says(Good);

        await using var session = harness.Build();

        await session.RunAsync(
            Conversation(harness, maxRepairs: 1),
            null,
            TestContext.Current.CancellationToken);

        var second = harness.Provider.Requests[1].Messages;

        // System prompt, the opening message, the rejected answer, and the rejection. Nothing was
        // thrown away to make room for the repair.
        second.Count.ShouldBeGreaterThan(harness.Provider.Requests[0].Messages.Count);
        second[0].Text.ShouldBe(harness.Provider.Requests[0].Messages[0].Text);
        second.ShouldContain(message => message.Text!.Contains("\"summary\":\"\""));
    }

    [Fact]
    public async Task When_the_repairs_run_out_the_run_fails_and_carries_the_answer_it_refused()
    {
        var harness = new SessionHarness();
        harness.Provider.Says(Bad).Says(Bad).Says(Bad);

        await using var session = harness.Build();

        var run = await session.RunAsync(
            Conversation(harness, maxRepairs: 2),
            null,
            TestContext.Current.CancellationToken);

        run.Succeeded.ShouldBeFalse();
        run.FailureCode.ShouldBe(LlmFailures.ResultRejected);
        run.ResultRepairs.ShouldBe(2);

        // The refused answer travels with the failure, so the user can be shown what came back
        // rather than only that something did.
        run.StructuredJson.ShouldNotBeNull().ShouldContain("\"summary\":\"\"");
        run.ProviderMessage.ShouldNotBeNull().ShouldContain("src/Cache.cs");

        // Three attempts, not four: the cap is on repairs, and the first answer is not one.
        harness.Provider.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_cap_of_zero_fails_on_the_first_rejection_without_asking_again()
    {
        var harness = new SessionHarness();
        harness.Provider.Says(Bad);

        await using var session = harness.Build();

        var run = await session.RunAsync(
            Conversation(harness, maxRepairs: 0),
            null,
            TestContext.Current.CancellationToken);

        run.FailureCode.ShouldBe(LlmFailures.ResultRejected);
        run.ResultRepairs.ShouldBe(0);
        harness.Provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_schema_violation_and_a_rejection_are_different_failures()
    {
        // Distinct codes because they lead somewhere different: one is the provider getting the
        // shape wrong, the other is the content being wrong in a shape that was fine.
        var harness = new SessionHarness();
        harness.Provider.Says("not json at all").Says("still not json");

        await using var session = harness.Build();

        var run = await session.RunAsync(
            Conversation(harness, maxRepairs: 2),
            null,
            TestContext.Current.CancellationToken);

        run.FailureCode.ShouldBe(LlmFailures.InvalidResponse);
        run.ResultRepairs.ShouldBe(0);
    }

    /// <summary>
    /// A conversation whose validator rejects an empty summary, reporting two problems phrased the
    /// way <c>AnalysisValidator</c> phrases them — naming a path and a container id.
    /// </summary>
    private static LlmConversation Conversation(
        SessionHarness harness,
        int maxRepairs,
        IReadOnlyList<LlmToolDefinition>? tools = null) =>
        SessionHarness.Conversation(tools, SessionHarness.AnswerFormat) with
        {
            MaxResultRepairs = maxRepairs,
            ResultValidator = json => json.Contains("\"summary\":\"\"")
                ?
                [
                    "No node covers 'src/Cache.cs'.",
                    "Container 'core' has two entry nodes.",
                ]
                : [],
        };
}
