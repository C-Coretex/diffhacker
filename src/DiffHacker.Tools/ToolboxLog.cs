using Microsoft.Extensions.Logging;

namespace DiffHacker.Tools;

/// <summary>
/// Requirement 5: every tool call and the size of what it returned.
/// <para>
/// One implementation, both consumption paths. The in-process path already records tool calls on
/// <c>LlmToolCallRecord</c> for the run trace, but the stdio path has no session to record into,
/// and cost analysis after the fact needs both to look the same.
/// </para>
/// <para>
/// Sizes and durations, never content. A tool result is repository content by definition, and
/// <c>log.txt</c> is not where it belongs — the argument preview is capped at the same 200
/// characters the run trace uses.
/// </para>
/// </summary>
internal static partial class ToolboxLog
{
    /// <summary>Matches <c>LlmToolCallRecord.PreviewLength</c>, so the log and the trace agree.</summary>
    public const int PreviewLength = 200;

    public static void Called(
        ILogger logger,
        string tool,
        string arguments,
        int resultBytes,
        double milliseconds,
        bool truncated,
        bool failed) =>
        ToolCalled(logger, tool, Preview(arguments), resultBytes, milliseconds, truncated, failed);

    public static string Preview(string? arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            return string.Empty;
        }

        return arguments.Length <= PreviewLength
            ? arguments
            : arguments[..PreviewLength] + "…";
    }

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Debug,
        Message = "Tool {Tool}({Arguments}) returned {ResultBytes} byte(s) in {Milliseconds:0.#} ms "
            + "(truncated: {Truncated}, failed: {Failed})")]
    private static partial void ToolCalled(
        ILogger logger,
        string tool,
        string arguments,
        int resultBytes,
        double milliseconds,
        bool truncated,
        bool failed);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Warning,
        Message = "Tool {Tool} threw. The model was told it failed rather than the run ending.")]
    public static partial void ToolThrew(ILogger logger, string tool, Exception exception);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Information,
        Message = "Toolbox ready for {Repository}: {ToolCount} tools, {ChangedFiles} changed file(s), "
            + "{VisibleFiles} visible file(s).")]
    public static partial void ToolboxReady(
        ILogger logger,
        string repository,
        int toolCount,
        int changedFiles,
        int visibleFiles);
}
