using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using Microsoft.Extensions.AI;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// How full the context is, and what is filling it — Iteration 8 requirements 14 and 16.
/// <para>
/// The distinction these tests exist to hold is between two numbers that look alike and are not:
/// <see cref="LlmUsage.InputTokens"/> on the cumulative usage is the running total for the whole
/// run and only ever rises, while <see cref="LlmRunEvent.ContextTokens"/> is the size of the last
/// single request and falls when tool results are pruned. Confusing them would make the meter read
/// as if the context never emptied.
/// </para>
/// <para>
/// And the second distinction: the total is the provider's own token count, the breakdown is our
/// own character count. Neither is estimated, and the two are different units on purpose.
/// </para>
/// </summary>
public sealed class LlmContextTests
{
    [Fact]
    public async Task The_context_size_is_the_last_requests_input_tokens_not_the_running_total()
    {
        var harness = new SessionHarness();
        harness.Provider
            .Calls(("echo", new { text = "a" }))
            .Says("done", Usage(input: 900, output: 5));

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            harness.Progress,
            TestContext.Current.CancellationToken);

        var last = harness.Events.Last(e => e.ContextTokens is not null);

        // Two requests were made, so the cumulative total is larger than either one of them.
        last.ContextTokens.ShouldBe(900);
        last.CumulativeUsage.InputTokens.ShouldBeGreaterThan(900);
    }

    [Fact]
    public async Task The_context_size_is_absent_rather_than_zero_when_the_provider_reports_nothing()
    {
        // Zero would read as an empty context, which is the opposite of unknown.
        var harness = new SessionHarness();
        harness.Provider.Responds(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
        {
            Usage = null,
            FinishReason = ChatFinishReason.Stop,
        });

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation(),
            harness.Progress,
            TestContext.Current.CancellationToken);

        harness.Events.ShouldAllBe(e => e.ContextTokens == null);
    }

    [Fact]
    public async Task The_breakdown_accounts_for_the_whole_of_what_was_sent()
    {
        // The categories partition the request. If they did not, the percentages beside them would
        // be percentages of nothing in particular.
        var harness = new SessionHarness();
        harness.Provider
            .Calls(("echo", new { text = "hello" }))
            // A valid answer, so no repair round runs. A repair prompt is a user message and would
            // legitimately grow UserCharacters past the opening message.
            .Says("""{"summary":"It renames a method.","confidence":4}""");

        var conversation = SessionHarness.Conversation(
            [SessionHarness.EchoTool()],
            SessionHarness.AnswerFormat);

        await using var session = harness.Build();
        await session.RunAsync(conversation, harness.Progress, TestContext.Current.CancellationToken);

        var context = harness.Events.Last(e => e.Context is not null).Context!.Value;

        context.InstructionCharacters.ShouldBe(conversation.SystemPrompt.Length);
        context.UserCharacters.ShouldBe(conversation.UserMessage.Length);

        // The schema reaches the model twice on a native-mode request: appended to the prompt and
        // sent as the response format. Both are paid for on every turn, so both are counted.
        context.SchemaCharacters.ShouldBeGreaterThan(SessionHarness.AnswerFormat.SchemaJson.Length);

        var tool = SessionHarness.EchoTool();
        context.ToolDefinitionCharacters.ShouldBe(
            tool.Name.Length + tool.Description.Length + tool.ParametersSchemaJson.Length);

        // The echo tool returns its own arguments, so the result is in the transcript.
        context.ToolResultCharacters.ShouldBeGreaterThan(0);
        context.AssistantCharacters.ShouldBeGreaterThan(0);
        context.TotalCharacters.ShouldBeGreaterThan(conversation.SystemPrompt.Length);
    }

    [Fact]
    public async Task Reasoning_is_absent_when_the_provider_never_surfaces_any()
    {
        // Most providers bill for reasoning tokens and never show them. Reporting zero would claim
        // the model reasoned in no characters at all, which is a different and false claim.
        var harness = new SessionHarness();
        harness.Provider.Says("done");

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation(),
            harness.Progress,
            TestContext.Current.CancellationToken);

        harness.Events.Last(e => e.Context is not null).Context!.Value.ReasoningCharacters
            .ShouldBeNull();
    }

    [Fact]
    public async Task Reasoning_is_counted_when_the_provider_does_surface_it()
    {
        var harness = new SessionHarness();
        harness.Provider.Responds(new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new TextReasoningContent("thinking about it"), new TextContent("done")]))
        {
            Usage = Usage(10, 5),
            FinishReason = ChatFinishReason.Stop,
        });

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation(),
            harness.Progress,
            TestContext.Current.CancellationToken);

        harness.Events.Last(e => e.Context is not null).Context!.Value.ReasoningCharacters
            .ShouldBe("thinking about it".Length);
    }

    [Fact]
    public async Task Pruning_moves_characters_out_of_the_transcript_and_into_the_pruned_total()
    {
        // The two halves have to move together: what pruning removed stops being context and starts
        // being the number that explains why the model forgot something.
        var harness = new SessionHarness
        {
            // Sized so one result fits and two do not: the first survives its own turn and is
            // pruned on the next, which is what gives the assertion a before and an after. A budget
            // small enough to prune everything immediately would never show a peak.
            Budget = LlmBudget.Default with { ToolResultRetentionBytes = 600 },
        };

        harness.Provider
            .Calls(("echo", new { text = new string('a', 500) }))
            .Calls(("echo", new { text = new string('b', 500) }))
            .Says("done");

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            harness.Progress,
            TestContext.Current.CancellationToken);

        var last = harness.Events.Last(e => e.Context is not null).Context!.Value;

        // Everything the tools ever returned. ASCII, so bytes and characters agree.
        var produced = harness.Events
            .Where(e => e.Kind == LlmRunEventKind.ToolCallFinished)
            .Sum(e => e.ResultBytes ?? 0);

        last.PrunedCharacters.ShouldBeGreaterThan(0);

        // What is left is genuinely smaller than what went in, rather than the pruned count being
        // added alongside an unchanged total.
        last.ToolResultCharacters.ShouldBeLessThan(produced);

        // And the two accounts add back up to it: nothing is lost, and nothing is counted twice.
        // The stub left in place of a pruned result is why this is exact rather than approximate —
        // pruning records the difference it made, not the whole of what it replaced.
        (last.ToolResultCharacters + last.PrunedCharacters).ShouldBe(produced);
    }

    [Fact]
    public async Task The_context_window_comes_from_the_catalogue_when_the_profile_says_nothing()
    {
        var harness = new SessionHarness();
        harness.Provider.Says("done");

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation(),
            harness.Progress,
            TestContext.Current.CancellationToken);

        harness.Events.Last(e => e.ContextWindowTokens is not null).ContextWindowTokens
            .ShouldBe(100_000);
    }

    [Fact]
    public async Task A_profile_override_beats_the_catalogue()
    {
        // Resolution order is exactly cost's: the user's number wins, because the bundled table is
        // a snapshot and goes stale.
        var harness = new SessionHarness
        {
            Profile = SessionHarness.ProfileFor(LlmProviderType.OpenAi) with
            {
                ContextWindowTokens = 1_000_000,
            },
        };

        harness.Provider.Says("done");

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation(),
            harness.Progress,
            TestContext.Current.CancellationToken);

        harness.Events.Last(e => e.Context is not null).ContextWindowTokens.ShouldBe(1_000_000);
    }

    [Fact]
    public async Task An_unknown_window_is_reported_as_unknown_rather_than_guessed()
    {
        var harness = new SessionHarness
        {
            Profile = SessionHarness.ProfileFor(LlmProviderType.OpenAi, "unknown-window-model"),
        };

        harness.Provider.Says("done");

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation(),
            harness.Progress,
            TestContext.Current.CancellationToken);

        harness.Events.ShouldAllBe(e => e.ContextWindowTokens == null);
    }

    [Fact]
    public async Task A_run_whose_context_exceeds_the_window_is_not_stopped()
    {
        // Requirement 16 says information, not cap. This is the assertion that keeps it that way:
        // wiring the window into the budget is exactly the helpful-looking change a later reader
        // would make, and it would turn a meter into a kill switch nobody asked for.
        var harness = new SessionHarness
        {
            Profile = SessionHarness.ProfileFor(LlmProviderType.OpenAi) with
            {
                ContextWindowTokens = 100,
            },
        };

        harness.Provider
            .Calls(("echo", new { text = "a" }))
            .Says("done", Usage(input: 5_000_000, output: 10));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            harness.Progress,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(
            LlmRunOutcome.Completed,
            "the context window is displayed, never enforced.");

        harness.Events.Last(e => e.ContextTokens is not null).ContextTokens!.Value
            .ShouldBeGreaterThan(harness.Profile.ContextWindowTokens!.Value);
    }

    private static UsageDetails Usage(long input, long output) =>
        new() { InputTokenCount = input, OutputTokenCount = output };
}
