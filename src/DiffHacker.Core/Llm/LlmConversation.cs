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

    /// <summary>
    /// Checked against the answer once it already satisfies <see cref="ResponseFormat"/>, for the
    /// rules a JSON Schema cannot express. Returns one message per problem, each naming the thing
    /// it is about; an empty list means the answer is accepted.
    /// <para>
    /// The check happens <i>inside</i> the run rather than after it, and that is the point. The
    /// failures are handed back into the same conversation, so the model still has its whole
    /// exploration in context and its tools still bound — it can go and read the file it forgot
    /// rather than being asked to fix a document from memory in a fresh session. Null, the
    /// default, means the schema is the only gate.
    /// </para>
    /// </summary>
    public Func<string, IReadOnlyList<string>>? ResultValidator { get; init; }

    /// <summary>
    /// How many times a result rejected by <see cref="ResultValidator"/> may be handed back.
    /// Once spent — and at zero, immediately — the run fails with
    /// <see cref="LlmFailures.ResultRejected"/> rather than returning an answer that did not pass.
    /// </summary>
    public int MaxResultRepairs { get; init; }

    /// <summary>
    /// How many times an answer that does not match <see cref="ResponseFormat"/>'s JSON Schema
    /// may be handed back before the run fails with <see cref="LlmFailures.InvalidResponse"/>.
    /// Defaults to one so a caller that never sets it keeps today's behaviour: a single
    /// structural mistake is usually a formatting slip the model fixes from what it already
    /// wrote, and a caller with a lot to get right — many array entries, each independently
    /// liable to a small mistake — can ask for more.
    /// </summary>
    public int MaxSchemaRepairs { get; init; } = 1;
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
