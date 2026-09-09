namespace DiffHacker.Core.Llm;

/// <summary>
/// What is filling the context, by category, in characters.
/// <para>
/// <b>Why characters and not tokens.</b> The total occupancy of the context is reported in tokens
/// because the provider counts those and tells us; nothing here could count them without shipping
/// a tokeniser per provider and still being wrong on the next model. Characters, on the other
/// hand, are exact and free — the application built every one of these strings. Mixing the two
/// units in one display is deliberate and the display says so: one exact number from the provider,
/// and an exact proportional breakdown of our own. An estimated per-category token count presented
/// beside the provider's real total would be read as the same kind of number, and it is not.
/// </para>
/// <para>
/// The categories partition the request. Everything sent on a turn is in exactly one of them, so
/// they sum to the whole and a reader can trust the proportions — with the single exception of
/// <see cref="PrunedCharacters"/>, which is what has been <i>removed</i> and is therefore not part
/// of anything.
/// </para>
/// </summary>
public readonly record struct LlmContextBreakdown
{
    /// <summary>The system prompt, without the copy of the response schema appended to it.</summary>
    public int InstructionCharacters { get; init; }

    /// <summary>
    /// The response schema: the copy embedded in the system prompt, plus the copy sent as the
    /// response format or as the submit tool's parameters when the mode in use sends one.
    /// </summary>
    public int SchemaCharacters { get; init; }

    /// <summary>Tool names, descriptions and parameter schemas, re-sent on every turn.</summary>
    public int ToolDefinitionCharacters { get; init; }

    /// <summary>
    /// Everything the application has said as the user: the opening message, and any correction
    /// it asked for afterwards.
    /// </summary>
    public int UserCharacters { get; init; }

    /// <summary>Tool results still in the transcript. The only category pruning touches.</summary>
    public int ToolResultCharacters { get; init; }

    /// <summary>
    /// Tool-result text pruning has removed so far, counted cumulatively. Not part of the context
    /// any more; reported so a reader can see what retention is holding back as well as what it
    /// kept.
    /// </summary>
    public int PrunedCharacters { get; init; }

    /// <summary>The model's own text and the arguments of its tool calls.</summary>
    public int AssistantCharacters { get; init; }

    /// <summary>
    /// Reasoning the provider surfaced as content of its own, or null when it never has. Null is
    /// "not reported", which is a different claim from the model having reasoned in no characters
    /// at all — most providers bill for reasoning tokens they never show us.
    /// </summary>
    public int? ReasoningCharacters { get; init; }

    /// <summary>
    /// Everything currently in the request. Excludes <see cref="PrunedCharacters"/>, which is by
    /// definition no longer there.
    /// </summary>
    public int TotalCharacters =>
        InstructionCharacters
        + SchemaCharacters
        + ToolDefinitionCharacters
        + UserCharacters
        + ToolResultCharacters
        + AssistantCharacters
        + (ReasoningCharacters ?? 0);
}
