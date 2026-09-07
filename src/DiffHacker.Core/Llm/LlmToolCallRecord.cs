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

    /// <summary>
    /// The start of what the tool returned, truncated to <see cref="ResultPreviewLength"/>.
    /// <para>
    /// Added in Iteration 6, where the live view shows the user what each call actually answered.
    /// Still a preview and not the result: a single call may return 48 KiB and a run may make five
    /// hundred of them, so keeping every byte to display a few would be the odd trade this record
    /// already avoids for arguments.
    /// </para>
    /// </summary>
    public string ResultPreview { get; init; } = string.Empty;

    public required TimeSpan Duration { get; init; }

    /// <summary>Whether the tool reported a failure the model was expected to react to.</summary>
    public bool IsError { get; init; }

    /// <summary>How much of an argument list is worth keeping.</summary>
    public const int PreviewLength = 200;

    /// <summary>
    /// How much of a result is worth keeping. Longer than the argument preview because a result is
    /// where the interesting part is — a header line and the first few rows say whether the call
    /// found anything, which is the question someone watching a run is asking.
    /// </summary>
    public const int ResultPreviewLength = 500;

    /// <summary>Truncates an argument list to <see cref="PreviewLength"/>, marking the cut.</summary>
    public static string Preview(string? argumentsJson) => Truncate(argumentsJson, PreviewLength);

    /// <summary>Truncates a result to <see cref="ResultPreviewLength"/>, marking the cut.</summary>
    public static string PreviewResult(string? content) => Truncate(content, ResultPreviewLength);

    private static string Truncate(string? text, int limit)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= limit ? text : text[..limit] + "…";
    }
}
