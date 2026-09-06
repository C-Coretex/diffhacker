namespace DiffHacker.Tools;

/// <summary>
/// How much any one tool may hand back.
/// <para>
/// These numbers are economics, not tuning. <c>LlmBudget</c> defaults to 500 tool calls and
/// 2,000,000 tokens for a whole run, so the <i>average</i> result has to land near 4,000 tokens
/// for a long analysis to fit inside it. The defaults below put a typical page at 5–25 KB, call
/// it 1–6k tokens, which leaves the hard ceiling as something a pathological result hits rather
/// than something every call spends.
/// </para>
/// <para>
/// Bytes rather than tokens because bytes are deterministic and tokenisers are not. Roughly four
/// UTF-8 bytes to the token for source code is the rule of thumb behind the arithmetic.
/// </para>
/// </summary>
public sealed record ToolboxLimits
{
    public static ToolboxLimits Default { get; } = new();

    /// <summary>
    /// The ceiling no tool result may cross, whatever its own page size says. 48 KiB is about
    /// 12k tokens: a large but still readable observation, and under a tenth of a 200k context.
    /// </summary>
    public int MaxResultBytes { get; init; } = 48 * 1024;

    public int ChangedFilesPageSize { get; init; } = 150;

    public int ChangedFilesMaxPageSize { get; init; } = 500;

    public int DiffPathsPerCall { get; init; } = 10;

    public int DiffTotalBytes { get; init; } = 32 * 1024;

    public int DiffBytesPerFile { get; init; } = 16 * 1024;

    public int ReadFileLines { get; init; } = 400;

    public int ReadFileMaxLines { get; init; } = 2000;

    public int ReadFileBytes { get; init; } = 32 * 1024;

    public int SearchMatches { get; init; } = 40;

    public int SearchMaxMatches { get; init; } = 200;

    public int SearchContextLines { get; init; } = 2;

    public int SearchMaxContextLines { get; init; } = 8;

    public int FindFilesPageSize { get; init; } = 200;

    public int FindFilesMaxPageSize { get; init; } = 1000;

    public int DirectoryPageSize { get; init; } = 300;

    public int TreeDepth { get; init; } = 2;

    public int TreeMaxDepth { get; init; } = 10;

    public int TreeEntries { get; init; } = 500;

    public int PathInfoPerCall { get; init; } = 50;

    /// <summary>
    /// Clamps a caller-supplied page size into range. A model that asks for 100,000 rows gets the
    /// maximum and a note, not an error: the request was reasonable, the number was not.
    /// </summary>
    public static int Clamp(int? requested, int fallback, int maximum) =>
        requested is null or < 1 ? fallback : Math.Min(requested.Value, maximum);
}
