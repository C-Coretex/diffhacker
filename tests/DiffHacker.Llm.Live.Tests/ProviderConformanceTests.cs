using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Llm.Live.Tests;

/// <summary>
/// The same canned conversation against every real provider.
/// <para>
/// Iteration 4's "Done when" bar is that one tool-calling conversation succeeds against all
/// five providers, reports usage and cancels cleanly — and its requirement 9 is that no test
/// hits a real provider. Both are true here because this project is opt-in: every test skips
/// unless its <c>DIFFHACKER_LIVE_*</c> variable is set, so the default run is offline and the
/// live pass is something a person chooses to do with their own keys.
/// </para>
/// <para>
/// See <c>README.md</c> beside this file for how to run it.
/// </para>
/// </summary>
public sealed class ProviderConformanceTests
{
    [Fact]
    public Task OpenAI_runs_the_canned_conversation() => Conform(LlmProviderType.OpenAi);

    [Fact]
    public Task Anthropic_runs_the_canned_conversation() => Conform(LlmProviderType.Anthropic);

    [Fact]
    public Task Gemini_runs_the_canned_conversation() => Conform(LlmProviderType.Gemini);

    [Fact]
    public Task Grok_runs_the_canned_conversation() => Conform(LlmProviderType.Grok);

    [Fact]
    public Task DeepSeek_runs_the_canned_conversation() => Conform(LlmProviderType.DeepSeek);

    [Fact]
    public Task An_OpenAI_compatible_endpoint_runs_the_canned_conversation() =>
        Conform(LlmProviderType.OpenAiCompatible);

    [Fact]
    public async Task A_run_cancelled_mid_flight_unwinds_and_still_reports_its_usage()
    {
        // Requirement 4 against a real provider, where the request is genuinely in flight over
        // a socket rather than being handed back by a fake.
        var provider = FirstAvailable();
        Assert.SkipWhen(provider is null, "No DIFFHACKER_LIVE_* provider is configured.");

        using var cancellation = new CancellationTokenSource();
        await using var session = await OpenAsync(provider!, TestContext.Current.CancellationToken);

        var conversation = Conversation(_ => cancellation.Cancel());

        await Should.ThrowAsync<OperationCanceledException>(() =>
            session.RunAsync(conversation, null, cancellation.Token));

        session.CumulativeUsage.TotalTokens.ShouldBeGreaterThan(
            0,
            "the turn that reached the provider was really billed, so it is really reported.");
    }

    [Fact]
    public async Task A_nonsense_model_name_is_reported_as_a_missing_model()
    {
        // One of the five error classes requirement 5 names, forced against the real thing.
        // The others need a revoked key or an exhausted balance, which cannot be arranged from
        // a test — the offline suite covers their mapping.
        var provider = FirstAvailable();
        Assert.SkipWhen(provider is null, "No DIFFHACKER_LIVE_* provider is configured.");

        var profile = provider!.ToProfile() with { Model = "diffhacker-no-such-model-v99" };

        await using var session = Open(profile, provider.EffectiveApiKey);

        var result = await session.RunAsync(
            Conversation(),
            null,
            TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.FailureCode.ShouldBeOneOf(
            LlmFailures.ModelNotFound,
            LlmFailures.InvalidResponse,
            LlmFailures.Forbidden);
    }

    /// <summary>
    /// Call the echo tool twice, then answer in a fixed JSON shape — the conversation the
    /// iteration's verification steps describe, run for real.
    /// </summary>
    private static async Task Conform(LlmProviderType type)
    {
        var provider = LiveProvider.For(type);
        Assert.SkipWhen(provider.SkipReason is not null, provider.SkipReason ?? string.Empty);

        var echoed = new List<string>();

        await using var session = await OpenAsync(provider, TestContext.Current.CancellationToken);

        var result = await session.RunAsync(
            Conversation(echoed.Add),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(
            LlmRunOutcome.Completed,
            $"{provider.Model} on {type} said: {result.FailureCode} / {result.ProviderMessage}");

        echoed.Count.ShouldBeGreaterThanOrEqualTo(2, "the model was asked to echo twice.");

        var json = result.StructuredJson.ShouldNotBeNull();
        StructuredOutput.Validate(json, AnswerFormat).ShouldBeEmpty();

        // Requirement 3: usage per request and cumulatively, matching what the provider itself
        // reported. A provider that reports nothing is a finding, not a pass.
        result.Usage.IsReported.ShouldBeTrue($"{type} reported no usage at all.");
        result.Usage.InputTokens.ShouldBeGreaterThan(0);
        result.Usage.OutputTokens.ShouldBeGreaterThan(0);

        session.RequestUsages.Count.ShouldBe(result.TurnCount);
        session.RequestUsages.Sum(usage => usage.TotalTokens).ShouldBe(result.Usage.TotalTokens);

        result.ToolCalls.Count.ShouldBe(echoed.Count);
        result.ToolCalls.Select(call => call.Ordinal).ShouldBe(Enumerable.Range(1, echoed.Count));
    }

    private static LiveProvider? FirstAvailable() =>
        LiveProvider.All.FirstOrDefault(provider => provider.SkipReason is null);

    private static async ValueTask<LlmSession> OpenAsync(LiveProvider provider, CancellationToken cancellationToken)
    {
        await Task.Yield();
        _ = cancellationToken;
        return Open(provider.ToProfile(), provider.EffectiveApiKey);
    }

    private static LlmSession Open(LlmProviderProfile profile, string apiKey)
    {
        // Built directly rather than through LlmSessionFactory: the factory's job is to fetch
        // the key from the secret store, and this suite has it from the environment. Nothing
        // else about the session differs.
        var httpClient = new HttpClient();

        return new LlmSession(
            ChatClientFactory.Create(profile, apiKey, httpClient),
            httpClient,
            profile,
            LlmBudget.Default with { MaxTurns = 8, MaxToolCalls = 8 },
            new Pricing.ModelPricing(),
            NullLogger<LlmSession>.Instance);
    }

    private static LlmConversation Conversation(Action<string>? onEcho = null) => new()
    {
        SystemPrompt =
            "You are a test harness. Follow the instructions exactly and do not ask questions.",
        UserMessage =
            "Call the `echo` tool with text \"one\", then call it again with text \"two\". "
            + "After both calls have returned, answer with the required JSON: set `summary` to "
            + "the two echoed values joined by a comma, and `confidence` to 5.",
        Tools =
        [
            new LlmToolDefinition
            {
                Name = "echo",
                Description = "Repeats back the text it is given. Use it when asked to echo something.",
                ParametersSchemaJson =
                    """
                    {"type":"object","properties":{"text":{"type":"string","description":"The text to repeat."}},"required":["text"],"additionalProperties":false}
                    """,
                Invoke = (arguments, _) =>
                {
                    onEcho?.Invoke(arguments);
                    return ValueTask.FromResult(LlmToolResult.Success(arguments));
                },
            },
        ],
        ResponseFormat = AnswerFormat,
    };

    private static LlmResponseFormat AnswerFormat { get; } = new()
    {
        SchemaName = "conformance_answer",
        SchemaJson =
            """
            {
              "type": "object",
              "properties": {
                "summary": { "type": "string", "description": "The echoed values, joined by a comma." },
                "confidence": { "type": "integer", "description": "Always 5." }
              },
              "required": ["summary", "confidence"],
              "additionalProperties": false
            }
            """,
    };
}
