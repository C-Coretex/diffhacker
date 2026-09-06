using System.ComponentModel;
using System.Globalization;
using DiffHacker.Core.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DiffHacker.Tools.Tools;

/// <summary>
/// The two tools that are not about reading the repository: standing knowledge about it, and
/// telling the reviewer what the model is doing.
/// </summary>
[McpServerToolType]
public sealed partial class ContextTools(
    RepositorySession session,
    IProjectProfileSource profiles,
    IToolProgressSink progress,
    ILogger<ContextTools> logger)
{
    private int _sequence;

    [McpServerTool(Name = "get_project_profile", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Returns the stored profile for this repository: what it is, how it is laid out, the
        conventions it follows, and anything previously learned about it.

        Call this first, before exploring. It is free, and when a profile exists it will save you
        a great many calls.

        When no profile has been stored, that is what it says. Explore the repository yourself in
        that case — get_repository_tree and the manifest files it shows you are the usual start.
        """)]
    public async Task<string> GetProjectProfileAsync(CancellationToken cancellationToken = default)
    {
        var profile = await profiles.GetAsync(session.Root, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(profile))
        {
            return profile;
        }

        return "No profile has been stored for this repository yet.\n"
            + "Explore it yourself: get_repository_tree shows the layout, and the manifest files it "
            + "reveals (package.json, pyproject.toml, *.csproj, go.mod and so on) name the projects "
            + "and their dependencies. README files are usually worth one read_file.";
    }

    [McpServerTool(Name = "report_progress", ReadOnly = true, OpenWorld = false)]
    [Description(
        """
        Tells the reviewer what you are doing right now. Call it whenever you move to a new stage
        of your work — not for every tool call, but every time the honest answer to "what is it
        doing?" changes.

        The person waiting sees this text. Without it they see a spinner and have no idea whether
        you are reading files, forming a picture, or nearly finished. Write for them: "reading the
        authentication changes", not "calling search_text".

        Returns immediately and never fails. It has no effect on your analysis; it is purely how
        the work becomes visible while it happens.
        """)]
    public async Task<string> ReportProgressAsync(
        [Description("One short sentence, in your own words, about what you are doing now.")]
        string message,
        [Description("Optional coarse stage: 'exploring', 'analysing', 'grouping', 'explaining', 'finishing'.")]
        string? phase = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "No message was given. Say what you are doing in one short sentence.";
        }

        var report = new ToolProgressReport(
            Interlocked.Increment(ref _sequence),
            message.Trim(),
            string.IsNullOrWhiteSpace(phase) ? null : phase.Trim().ToLowerInvariant(),
            DateTimeOffset.UtcNow);

        try
        {
            await progress.ReportAsync(report, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whether a notification reached a window is not something the model can act on, and
            // failing its call over it would cost a turn and teach it nothing.
            ProgressUndelivered(logger, ex);
        }

        return string.Create(CultureInfo.InvariantCulture, $"Recorded (#{report.Sequence}).");
    }

    [LoggerMessage(
        EventId = 5010,
        Level = LogLevel.Warning,
        Message = "A progress report could not be delivered.")]
    private static partial void ProgressUndelivered(ILogger logger, Exception exception);
}
