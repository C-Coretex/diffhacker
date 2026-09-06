using System.Collections.Concurrent;
using System.Text.Json;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using Microsoft.Extensions.AI;
using NJsonSchema;

namespace DiffHacker.Llm;

/// <summary>
/// How a provider is asked for JSON in a particular shape.
/// <para>
/// Ordered best to worst. <see cref="StructuredOutput.Downgrade"/> walks down it when a
/// provider rejects the mode it was offered.
/// </para>
/// </summary>
internal enum StructuredOutputMode
{
    /// <summary>A <c>json_schema</c> response format the provider enforces server-side.</summary>
    Native,

    /// <summary>
    /// A single tool whose parameter schema is the target schema. Tool arguments are the one
    /// place several providers apply strict schema enforcement even when their response format
    /// does not.
    /// </summary>
    ToolCall,

    /// <summary>Plain JSON mode: valid JSON guaranteed, the shape only asked for.</summary>
    JsonObject,

    /// <summary>Nothing but the prompt. The last resort, and the reason validation is not optional.</summary>
    PromptOnly,
}

/// <summary>
/// Getting schema-shaped JSON out of six providers that disagree about how to ask.
/// <para>
/// The spread is real. OpenAI and Grok enforce a <c>json_schema</c> response format; Gemini's
/// compatible surface accepts one; the Anthropic SDK translates one for us. DeepSeek's API
/// documents only <c>json_object</c> for a final message — but applies a strict schema to tool
/// arguments, which is why <see cref="StructuredOutputMode.ToolCall"/> exists rather than
/// dropping straight to prompting. A local Ollama or an arbitrary compatible endpoint could be
/// anywhere on that scale.
/// </para>
/// <para>
/// So the mode is a preference, not a promise: whatever the provider was asked for, the answer
/// is validated against the schema here before the run is called complete, and a run whose
/// answer does not fit gets one repair attempt with the validation errors handed back to the
/// model. That check is what makes the weaker tiers usable at all.
/// </para>
/// </summary>
internal static class StructuredOutput
{
    /// <summary>
    /// The tool used by <see cref="StructuredOutputMode.ToolCall"/>. Never dispatched — the
    /// session recognises it and treats the arguments as the final answer.
    /// </summary>
    public const string SubmitToolName = "submit_result";

    private static readonly ConcurrentDictionary<string, JsonSchema> SchemaCache = new(StringComparer.Ordinal);

    /// <summary>
    /// The best mode <paramref name="providerType"/> is known to support.
    /// </summary>
    public static StructuredOutputMode PreferredMode(LlmProviderType providerType) => providerType switch
    {
        LlmProviderType.OpenAi or LlmProviderType.Grok or LlmProviderType.Gemini => StructuredOutputMode.Native,

        // The Anthropic SDK's IChatClient translates a JSON schema response format into
        // whatever the Messages API currently wants, so this stays a Native ask from here.
        LlmProviderType.Anthropic => StructuredOutputMode.Native,

        // Documented as text or json_object only. Strict schemas do apply to tool arguments.
        LlmProviderType.DeepSeek => StructuredOutputMode.ToolCall,

        // Could be Ollama, could be a gateway, could be anything. Start optimistic and let the
        // downgrade path find the truth on the first rejection.
        LlmProviderType.OpenAiCompatible => StructuredOutputMode.Native,

        _ => StructuredOutputMode.JsonObject,
    };

    /// <summary>The next mode to try, or null when there is nothing weaker left.</summary>
    public static StructuredOutputMode? Downgrade(StructuredOutputMode mode) => mode switch
    {
        StructuredOutputMode.Native => StructuredOutputMode.ToolCall,
        StructuredOutputMode.ToolCall => StructuredOutputMode.JsonObject,
        StructuredOutputMode.JsonObject => StructuredOutputMode.PromptOnly,
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="failure"/> looks like the provider refusing the response format
    /// rather than refusing the request. Those get a downgrade instead of ending the run.
    /// </summary>
    public static bool IsUnsupportedFormat(LlmFailure failure) =>
        failure.HttpStatus is 400 or 404 or 422
        && failure.ProviderMessage is { } message
        && (message.Contains("response_format", StringComparison.OrdinalIgnoreCase)
            || message.Contains("json_schema", StringComparison.OrdinalIgnoreCase)
            || message.Contains("structured output", StringComparison.OrdinalIgnoreCase)
            || message.Contains("schema is not supported", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Configures <paramref name="options"/> to ask for <paramref name="format"/> in
    /// <paramref name="mode"/>. Returns the extra tool the mode needs, if any.
    /// </summary>
    public static AITool? Apply(ChatOptions options, LlmResponseFormat format, StructuredOutputMode mode)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(format);

        switch (mode)
        {
            case StructuredOutputMode.Native:
                options.ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    SchemaElement(format.SchemaJson),
                    format.SchemaName);
                return null;

            case StructuredOutputMode.ToolCall:
                options.ResponseFormat = null;

                // Auto, not RequireAny. The model still needs to call the repository tools
                // freely, and providers differ on whether a "must call something" instruction
                // survives past the first turn. The prompt says to submit; the session also
                // accepts a plain-text answer, so a model that ignores the tool is not lost.
                return SubmitTool(format);

            case StructuredOutputMode.JsonObject:
                options.ResponseFormat = ChatResponseFormat.Json;
                return null;

            default:
                options.ResponseFormat = null;
                return null;
        }
    }

    /// <summary>
    /// What to append to the system prompt so the model knows the shape even when the provider
    /// is not enforcing it.
    /// <para>
    /// Included in every mode, Native included. It costs a few hundred tokens once and it
    /// measurably improves the odds on providers whose enforcement is best-effort — which,
    /// behind a user-supplied base URL, is any of them.
    /// </para>
    /// </summary>
    public static string PromptSuffix(LlmResponseFormat format, StructuredOutputMode mode)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (mode is StructuredOutputMode.ToolCall)
        {
            return $"""


                When you have finished exploring, call the `{SubmitToolName}` tool exactly once
                with your complete answer as its arguments. Its argument schema is:

                {format.SchemaJson}
                """;
        }

        return $"""


            Your final answer must be a single JSON object conforming to this JSON Schema.
            Emit the JSON alone: no prose before it, no explanation after it, no code fence.

            {format.SchemaJson}
            """;
    }

    /// <summary>
    /// Validates <paramref name="json"/> against <paramref name="format"/>.
    /// </summary>
    /// <returns>The validation errors, empty when the document conforms.</returns>
    public static IReadOnlyList<string> Validate(string? json, LlmResponseFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (string.IsNullOrWhiteSpace(json))
        {
            return ["The response was empty."];
        }

        JsonSchema schema;
        try
        {
            schema = SchemaCache.GetOrAdd(format.SchemaJson, static text => JsonSchema.FromJsonAsync(text).GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Our own schema is unreadable. That is a build-time mistake, not a model failure,
            // and pretending the answer was wrong would send the reader looking in the wrong
            // place entirely.
            throw new InvalidOperationException(
                $"The schema '{format.SchemaName}' could not be parsed.", ex);
        }

        return [.. schema.Validate(json).Select(error => error.ToString())];
    }

    /// <summary>
    /// Pulls the JSON object out of a model's answer.
    /// <para>
    /// Models fence their JSON in Markdown even when told not to, especially in the weaker
    /// modes. Stripping a fence here is the difference between a usable answer and a repair
    /// round trip that costs a full context.
    /// </para>
    /// </summary>
    public static string? ExtractJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            if (firstNewline > 0 && lastFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }
        }

        // Anything else outside the object — a preamble sentence, a trailing note — is cut by
        // taking the outermost braces.
        var start = trimmed.IndexOf('{', StringComparison.Ordinal);
        var end = trimmed.LastIndexOf('}');

        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }

    private static SubmitResultFunction SubmitTool(LlmResponseFormat format) => new(format);

    private static JsonElement SchemaElement(string schemaJson) =>
        JsonDocument.Parse(schemaJson).RootElement.Clone();

    /// <summary>
    /// The submit tool, hand-rolled rather than built by <c>AIFunctionFactory</c>.
    /// <para>
    /// The factory derives a parameter schema from a delegate's signature, and the whole point
    /// here is the opposite: the parameter schema <i>is</i> the target schema, verbatim, so
    /// that providers which enforce tool arguments strictly enforce the shape we actually
    /// want.
    /// </para>
    /// </summary>
    private sealed class SubmitResultFunction(LlmResponseFormat format) : AIFunction
    {
        public override string Name => SubmitToolName;

        public override string Description =>
            "Submit your final answer. Call this exactly once, when you have finished "
            + "exploring, with the complete result as its arguments.";

        public override JsonElement JsonSchema { get; } = SchemaElement(format.SchemaJson);

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            // Unreachable: the session recognises this call by name and reads its arguments as
            // the answer rather than dispatching it. Throwing rather than returning is how a
            // mistake here becomes a failing test instead of a silently empty result.
            throw new InvalidOperationException(
                $"'{SubmitToolName}' is intercepted by the session and must never be invoked.");
        }
    }
}
