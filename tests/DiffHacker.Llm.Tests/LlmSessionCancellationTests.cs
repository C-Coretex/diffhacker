using DiffHacker.Core.Llm;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// Requirement 4: the user can stop a run at any point, the request chain unwinds cleanly, and
/// partial usage is still reported.
/// <para>
/// The last clause is why cancellation throws rather than returning a result. An
/// <see cref="OperationCanceledException"/> is what every caller of a token already expects,
/// and the accounting lives on the session, so a cancelled run can still be asked what it
/// spent — which is the part Iteration 13's cost view actually needs.
/// </para>
/// </summary>
public sealed class LlmSessionCancellationTests
{
    [Fact]
    public async Task Cancelling_mid_request_unwinds_rather_than_returning_a_failure()
    {
        var harness = new SessionHarness();
        harness.Provider.Gate = new TaskCompletionSource();
        harness.Provider.Says("never arrives");

        using var cancellation = new CancellationTokenSource();
        await using var session = harness.Build();

        var run = session.RunAsync(SessionHarness.Conversation(), null, cancellation.Token);

        // Wait for the request to be genuinely in flight before pulling the plug, so this
        // proves an unwind rather than a token checked before anything started.
        await WaitUntil(() => harness.Provider.Requests.Count == 1);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(run);
    }

    [Fact]
    public async Task A_cancelled_run_still_reports_what_it_spent()
    {
        var harness = new SessionHarness();
        harness.Provider
            .Calls(("echo", new { text = "x" }))
            .Says("never arrives");

        using var cancellation = new CancellationTokenSource();
        await using var session = harness.Build();

        var run = session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool(_ => cancellation.Cancel())]),
            null,
            cancellation.Token);

        await Should.ThrowAsync<OperationCanceledException>(run);

        session.CumulativeUsage.TotalTokens.ShouldBe(
            28,
            "the first turn's 20 input and 8 output tokens were really consumed and really billed.");
        session.RequestUsages.ShouldHaveSingleItem();
        session.ToolCalls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Cancelling_during_a_tool_call_stops_the_run()
    {
        var harness = new SessionHarness();
        harness.Provider.Calls(("slow", new { })).Says("unreached");

        using var cancellation = new CancellationTokenSource();

        var slowTool = new LlmToolDefinition
        {
            Name = "slow",
            Description = "Waits for the caller to give up.",
            ParametersSchemaJson = """{"type":"object","properties":{},"additionalProperties":false}""",
            Invoke = async (_, token) =>
            {
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
                return LlmToolResult.Success("unreached");
            },
        };

        await using var session = harness.Build();

        await Should.ThrowAsync<OperationCanceledException>(() => session.RunAsync(
            SessionHarness.Conversation([slowTool]),
            null,
            cancellation.Token));

        harness.Provider.Requests.Count.ShouldBe(
            1,
            "the loop must not go back to the provider after the caller gave up.");
    }

    [Fact]
    public async Task A_cancelled_run_leaves_no_unobserved_task_exception()
    {
        var observed = new List<Exception>();
        void Handler(object? sender, UnobservedTaskExceptionEventArgs args) =>
            observed.AddRange(args.Exception.InnerExceptions);

        TaskScheduler.UnobservedTaskException += Handler;

        try
        {
            var harness = new SessionHarness();
            harness.Provider.Gate = new TaskCompletionSource();
            harness.Provider.Says("never arrives");

            using var cancellation = new CancellationTokenSource();
            await using (var session = harness.Build())
            {
                var run = session.RunAsync(SessionHarness.Conversation(), null, cancellation.Token);
                await WaitUntil(() => harness.Provider.Requests.Count == 1);
                await cancellation.CancelAsync();
                await Should.ThrowAsync<OperationCanceledException>(run);
            }

            // Anything the run abandoned would be finalised here and reported as unobserved.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            observed.ShouldBeEmpty();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        // No fixed sleep: poll a condition with a ceiling, so a slow machine waits longer and a
        // fast one does not wait at all.
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Yield();
        }

        condition().ShouldBeTrue("the condition never became true within five seconds.");
    }
}
