namespace DiffHacker.Core.Llm;

/// <summary>
/// One tool the model may call, described the way the model will see it.
/// <para>
/// Arguments arrive as raw JSON text and results leave as text. That is deliberate: it is what
/// keeps this type — and therefore <c>DiffHacker.Core</c> — free of
/// <c>Microsoft.Extensions.AI</c> (§0.3), and it is the same currency Iteration 5's MCP tools
/// already speak, so the toolbox adapts onto this without a translation layer.
/// </para>
/// </summary>
public sealed record LlmToolDefinition
{
    /// <summary>
    /// The name the model calls. Providers vary in what they accept; lower_snake_case works
    /// everywhere.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// What the model reads to decide whether to call this. CLAUDE.md is explicit that these
    /// are prompt text, not API documentation.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// JSON Schema for the arguments object, as text. An empty-properties object schema means
    /// the tool takes none.
    /// </summary>
    public required string ParametersSchemaJson { get; init; }

    /// <summary>
    /// Runs the tool. <paramref name="argumentsJson"/> is whatever the model produced, which
    /// may not match <see cref="ParametersSchemaJson"/> — implementations validate rather than
    /// assume, and report a bad call as <see cref="LlmToolResult.Failure"/> so the model can
    /// correct itself instead of the run dying.
    /// </summary>
    public required Func<string, CancellationToken, ValueTask<LlmToolResult>> Invoke { get; init; }
}

/// <summary>
/// What a tool sends back to the model.
/// <para>
/// A failure is a result, not an exception. The model handed bad arguments is expected to try
/// again with better ones, and an exception would end the run instead of teaching it anything.
/// </para>
/// </summary>
public sealed record LlmToolResult
{
    /// <summary>The text handed back to the model. Never null; empty is a valid answer.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// Whether this is an error the model should react to. Providers that distinguish error
    /// results mark them; the rest get the text with the distinction stated in it.
    /// </summary>
    public bool IsError { get; init; }

    public static LlmToolResult Success(string content) =>
        new() { Content = content };

    public static LlmToolResult Failure(string content) =>
        new() { Content = content, IsError = true };
}
