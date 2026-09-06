using DiffHacker.Contracts;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using Microsoft.Extensions.AI;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// Requirement 7: JSON conforming to a schema from <c>/schema</c>, with per-provider handling of
/// how that is expressed.
/// <para>
/// The spread is real. OpenAI and Grok enforce a <c>json_schema</c> response format; the
/// Anthropic SDK translates one; DeepSeek's API documents only <c>json_object</c> for a final
/// message but applies a strict schema to tool arguments; a user-supplied endpoint could be
/// anywhere on that scale. So the mode is a preference, and validation is what makes the weaker
/// tiers usable at all.
/// </para>
/// </summary>
public sealed class StructuredOutputTests
{
    [Fact]
    public void Each_provider_is_asked_the_way_it_answers_best()
    {
        // One fact rather than a theory: StructuredOutputMode is internal to DiffHacker.Llm,
        // and a public [InlineData] parameter cannot name an internal type.
        StructuredOutput.PreferredMode(LlmProviderType.OpenAi).ShouldBe(StructuredOutputMode.Native);
        StructuredOutput.PreferredMode(LlmProviderType.Grok).ShouldBe(StructuredOutputMode.Native);
        StructuredOutput.PreferredMode(LlmProviderType.Gemini).ShouldBe(StructuredOutputMode.Native);
        StructuredOutput.PreferredMode(LlmProviderType.Anthropic).ShouldBe(StructuredOutputMode.Native);
        StructuredOutput.PreferredMode(LlmProviderType.OpenAiCompatible).ShouldBe(StructuredOutputMode.Native);

        StructuredOutput.PreferredMode(LlmProviderType.DeepSeek).ShouldBe(
            StructuredOutputMode.ToolCall,
            "its API documents text or json_object only, but enforces a strict schema on tool arguments.");
    }

    [Fact]
    public void The_tiers_degrade_all_the_way_down_and_then_stop()
    {
        StructuredOutput.Downgrade(StructuredOutputMode.Native).ShouldBe(StructuredOutputMode.ToolCall);
        StructuredOutput.Downgrade(StructuredOutputMode.ToolCall).ShouldBe(StructuredOutputMode.JsonObject);
        StructuredOutput.Downgrade(StructuredOutputMode.JsonObject).ShouldBe(StructuredOutputMode.PromptOnly);
        StructuredOutput.Downgrade(StructuredOutputMode.PromptOnly).ShouldBeNull();
    }

    [Fact]
    public void Native_mode_sends_the_schema_as_the_response_format()
    {
        var options = new ChatOptions();
        var extra = StructuredOutput.Apply(options, SessionHarness.AnswerFormat, StructuredOutputMode.Native);

        options.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
        extra.ShouldBeNull("nothing extra is needed when the provider enforces the shape itself.");
    }

    [Fact]
    public void Tool_call_mode_offers_a_submit_tool_whose_parameters_are_the_schema()
    {
        var options = new ChatOptions();
        var extra = StructuredOutput.Apply(options, SessionHarness.AnswerFormat, StructuredOutputMode.ToolCall);

        var tool = extra.ShouldBeAssignableTo<AIFunction>();
        tool!.Name.ShouldBe(StructuredOutput.SubmitToolName);

        // The whole point of the tier: providers that enforce tool arguments strictly then
        // enforce the shape we actually want.
        tool.JsonSchema.GetProperty("properties").TryGetProperty("summary", out _).ShouldBeTrue();
        options.ResponseFormat.ShouldBeNull();
    }

    [Fact]
    public void Json_object_mode_asks_only_for_valid_json()
    {
        var options = new ChatOptions();
        StructuredOutput.Apply(options, SessionHarness.AnswerFormat, StructuredOutputMode.JsonObject);

        options.ResponseFormat.ShouldBe(ChatResponseFormat.Json);
    }

    [Fact]
    public void The_schema_is_in_the_prompt_even_when_the_provider_enforces_it()
    {
        // A few hundred tokens once, against a measurable improvement on providers whose
        // enforcement turns out to be best-effort — which, behind a user-supplied base URL, is
        // any of them.
        var suffix = StructuredOutput.PromptSuffix(SessionHarness.AnswerFormat, StructuredOutputMode.Native);

        suffix.ShouldContain("confidence");
        suffix.ShouldContain("JSON Schema");
    }

    [Fact]
    public void Tool_call_mode_tells_the_model_to_submit_rather_than_to_answer()
    {
        var suffix = StructuredOutput.PromptSuffix(SessionHarness.AnswerFormat, StructuredOutputMode.ToolCall);

        suffix.ShouldContain(StructuredOutput.SubmitToolName);
    }

    [Fact]
    public void Validation_names_what_was_wrong()
    {
        var errors = StructuredOutput.Validate("""{"summary":"a rename"}""", SessionHarness.AnswerFormat);

        errors.ShouldNotBeEmpty();
        errors.ShouldContain(error => error.Contains("confidence", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_answer_is_a_validation_failure_not_a_pass(string? answer)
    {
        StructuredOutput.Validate(answer, SessionHarness.AnswerFormat).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_conforming_document_produces_no_errors()
    {
        StructuredOutput.Validate(
            """{"summary":"a rename","confidence":4}""",
            SessionHarness.AnswerFormat).ShouldBeEmpty();
    }

    [Fact]
    public void A_schema_from_the_contract_set_can_be_asked_for_directly()
    {
        // Requirement 7 says "a schema from /schema". Not a copy of one: the same document the
        // generator reads, embedded so it exists at run time too.
        var format = new LlmResponseFormat
        {
            SchemaName = "environment_info",
            SchemaJson = ContractSchemas.Get("environment-info"),
        };

        StructuredOutput.Validate(
            """
            {"gitAvailable":true,"secretBackend":"windows_dpapi","secretBackendIsFallback":false}
            """,
            format).ShouldBeEmpty();

        StructuredOutput.Validate("""{"gitAvailable":true}""", format).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("""{"a":1}""", """{"a":1}""")]
    [InlineData("```json\n{\"a\":1}\n```", """{"a":1}""")]
    [InlineData("```\n{\"a\":1}\n```", """{"a":1}""")]
    [InlineData("Here you go: {\"a\":1} — hope that helps.", """{"a":1}""")]
    public void A_fenced_or_prefaced_answer_is_unwrapped(string raw, string expected)
    {
        // Models fence their JSON even when told not to, especially in the weaker tiers.
        StructuredOutput.ExtractJson(raw).ShouldBe(expected);
    }

    [Fact]
    public void A_rejected_response_format_is_recognised_as_such()
    {
        var rejected = ProviderErrorMapper.Classify(FakeProviderResponse.OpenAiStyle(
            400,
            """{"error":{"message":"response_format json_schema is not supported by this model"}}"""));

        StructuredOutput.IsUnsupportedFormat(rejected).ShouldBeTrue();
    }

    [Fact]
    public void A_revoked_key_is_not_mistaken_for_a_rejected_format()
    {
        var rejected = ProviderErrorMapper.Classify(FakeProviderResponse.OpenAiStyle(401, "bad key"));

        StructuredOutput.IsUnsupportedFormat(rejected).ShouldBeFalse(
            "downgrading the response format would not help, and would hide the real problem.");
    }

    [Fact]
    public async Task A_provider_that_rejects_the_format_is_asked_again_more_simply()
    {
        var harness = new SessionHarness();
        harness.Provider
            .Throws(FakeProviderResponse.OpenAiStyle(
                400,
                """{"error":{"message":"response_format is not supported"}}"""))
            .Says("""{"summary":"a rename","confidence":4}""");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(format: SessionHarness.AnswerFormat),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(
            LlmRunOutcome.Completed,
            "a run must not die over how the question was phrased.");

        harness.Delays.ShouldBeEmpty("a downgrade is not a retry, so nothing is waited for.");
        harness.Provider.Requests[1].Options!.ResponseFormat.ShouldBeNull(
            "the second ask dropped to the tool-call tier.");
        harness.Provider.Requests[1].ToolNames.ShouldContain(StructuredOutput.SubmitToolName);
    }

    [Fact]
    public async Task A_submitted_tool_call_is_the_answer_rather_than_a_tool_to_run()
    {
        var harness = new SessionHarness
        {
            Profile = SessionHarness.ProfileFor(LlmProviderType.DeepSeek, model: "deepseek-chat"),
        };

        harness.Provider.Calls(
            (StructuredOutput.SubmitToolName, new { summary = "a rename", confidence = 4 }));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()], SessionHarness.AnswerFormat),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        result.StructuredJson.ShouldNotBeNull();
        result.StructuredJson!.ShouldContain("a rename");
        result.ToolCalls.ShouldBeEmpty("submitting an answer is not a tool call to trace.");
        harness.Provider.Requests.ShouldHaveSingleItem();
    }
}
