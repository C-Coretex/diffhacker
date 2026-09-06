namespace DiffHacker.Core.Llm;

/// <summary>
/// One tool call, after the fact.
/// <para>
/// This is the ordered trace Iteration 13's tool-call inspector renders, and the raw material
/// for working out where a run's tokens went. It records the <i>size</i> of the result rather
/// than the result itself: a run can return megabytes of file content, and keeping all of it
/// in memory to show a number would be an odd trade.
/// </para>
/// </summary>
public sealed record LlmToolCallRecord
{
    /// <summary>Position in the run, from 1. Ordering is the point of this record.</summary>
    public required int Ordinal { get; init; }

    /// <summary>Which turn asked for it. Several calls can share a turn.</summary>
    public required int Turn { get; init; }

    public required string ToolName { get; init; }

    /// <summary>
    /// The arguments the model sent, truncated. Long enough to tell two calls apart, short
    /// enough not to become a second copy of the conversation.
    /// </summary>
    public required string ArgumentsPreview { get; init; }

    /// <summary>Size of the result handed back, in UTF-8 bytes.</summary>
    public required int ResultBytes { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary>Whether the tool reported a failure the model was expected to react to.</summary>
    public bool IsError { get; init; }

    /// <summary>How much of an argument list is worth keeping.</summary>
    public const int PreviewLength = 200;

    /// <summary>Truncates an argument list to <see cref="PreviewLength"/>, marking the cut.</summary>
    public static string Preview(string? argumentsJson)
    {
        if (string.IsNullOrEmpty(argumentsJson))
        {
            return string.Empty;
        }

        return argumentsJson.Length <= PreviewLength
            ? argumentsJson
            : argumentsJson[..PreviewLength] + "…";
    }
}
