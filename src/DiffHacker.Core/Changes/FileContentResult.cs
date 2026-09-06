namespace DiffHacker.Core.Changes;

/// <summary>Which side of the comparison to read a file from.</summary>
public enum FileSide
{
    /// <summary>The file as committed at <c>HEAD</c>.</summary>
    Head,

    /// <summary>The file as it is on disk right now.</summary>
    WorkingTree,
}

/// <summary>
/// What kind of answer a content or diff request produced.
/// <para>
/// One union rather than nulls scattered across the record. A deleted file has no working-tree
/// side and an added file has no <c>HEAD</c> side, so <see cref="Absent"/> is an ordinary
/// answer, not an error — and it is the same shape that already carries "this is binary" and
/// "this is bigger than we will send". Iteration 10's diff viewer switches on this once.
/// </para>
/// </summary>
public enum FileContentKind
{
    /// <summary>Decoded text is available.</summary>
    Text,

    /// <summary>The content is binary. No text is provided; the size is.</summary>
    Binary,

    /// <summary>The file does not exist on the requested side.</summary>
    Absent,

    /// <summary>The file exists but exceeds <see cref="ContentLimits.MaxBytes"/>.</summary>
    TooLarge,
}

/// <summary>
/// Size ceilings for anything this layer will materialise as a string.
/// <para>
/// A cap is a product decision, not a defensive detail: above it the interface says "this file
/// is 41 MB" instead of hanging while it moves 41 MB across the JSON-RPC bridge. Five megabytes
/// is far past the point where a human reviews a text file line by line.
/// </para>
/// </summary>
public static class ContentLimits
{
    public const long MaxBytes = 5L * 1024 * 1024;

    /// <summary>
    /// Bytes sniffed for a NUL when deciding whether content is binary. Matches git's own
    /// heuristic, so a file this layer calls binary is a file git calls binary.
    /// </summary>
    public const int BinarySniffBytes = 8000;
}

/// <summary>The content of one file on one side of the comparison.</summary>
public sealed record FileContentResult
{
    public required FileContentKind Kind { get; init; }

    /// <summary>Decoded text, present only when <see cref="Kind"/> is <see cref="FileContentKind.Text"/>.</summary>
    public string? Text { get; init; }

    /// <summary>Size in bytes on the requested side. Zero when the file is absent.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Name of the encoding the bytes were decoded with, when they were. Reported rather than
    /// assumed so Iteration 5's toolbox and Iteration 10's viewer can both say what they did.
    /// </summary>
    public string? Encoding { get; init; }

    /// <summary>
    /// True when the bytes were not valid UTF-8 and a fallback encoding was used. The text is
    /// still readable, but it is a best effort and the interface should admit that.
    /// </summary>
    public bool UsedFallbackEncoding { get; init; }

    public static FileContentResult Absent() =>
        new() { Kind = FileContentKind.Absent, SizeBytes = 0 };

    public static FileContentResult Binary(long sizeBytes) =>
        new() { Kind = FileContentKind.Binary, SizeBytes = sizeBytes };

    public static FileContentResult TooLarge(long sizeBytes) =>
        new() { Kind = FileContentKind.TooLarge, SizeBytes = sizeBytes };
}
