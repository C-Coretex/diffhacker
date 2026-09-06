namespace DiffHacker.Core.Llm;

/// <summary>
/// Everything one run is started with.
/// <para>
/// Note how little there is: a system prompt, one opening message, the tools, and optionally
/// the shape the answer must take. §0.2.9 is the reason — the initial prompt carries the
/// changed-file list and instructions and nothing else, and the model pulls in whatever it
/// needs through <see cref="Tools"/>. Nothing here is a place to bulk-inject file contents.
/// </para>
/// </summary>
public sealed record LlmConversation
{
    /// <summary>Standing instructions. Sent as the system message where the provider has one.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>The opening user message.</summary>
    public required string UserMessage { get; init; }

    public IReadOnlyList<LlmToolDefinition> Tools { get; init; } = [];

    /// <summary>
    /// The shape the final answer must take, or null for free text. Supplied, it is enforced:
    /// the answer is validated against the schema before the run is called complete.
    /// </summary>
    public LlmResponseFormat? ResponseFormat { get; init; }
}

/// <summary>
/// A JSON Schema the model's final answer must satisfy.
/// <para>
/// The schema text normally comes from <c>DiffHacker.Contracts.ContractSchemas</c>, so the
/// thing the model is asked to produce and the thing the host deserialises are one document
/// rather than two that drift.
/// </para>
/// </summary>
public sealed record LlmResponseFormat
{
    /// <summary>
    /// Identifier sent to providers that name their schemas. Letters, digits and underscores;
    /// OpenAI rejects anything else.
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>The JSON Schema document, as text.</summary>
    public required string SchemaJson { get; init; }
}
