using System.ComponentModel;
using System.Diagnostics;
using DiffHacker.Core.Changes;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Host.Editor;

/// <summary>
/// Opens one changed file in an external editor, as a comparison where both sides exist and as a
/// single file at a line where they do not.
/// <para>
/// The renderer says which file and which editor; which of the two shapes runs is decided here, from
/// which sides of the file are actually on disk. That is deliberate: a renderer that chose could ask
/// for a comparison against a committed side that does not exist, and the answer to that would be an
/// editor showing an empty pane rather than a message.
/// </para>
/// <para>
/// Every launch is <c>UseShellExecute = false</c> with an argument list. No shell is involved, so a
/// path containing a space, a quote or a semicolon is one argument and cannot become two, and nothing
/// a user typed into the custom command can turn into a second command.
/// </para>
/// </summary>
public sealed partial class ExternalEditorLauncher(
    ExternalEditorLocator locator,
    HeadBlobExtractor extractor,
    IGitClient git,
    ILogger<ExternalEditorLauncher> logger)
{
    /// <summary>
    /// Launches the editor and returns once it has started — not once it has closed. <c>code</c>
    /// returns immediately and <c>devenv</c> does not, and neither exit code says anything about
    /// whether the reviewer got what they asked for.
    /// </summary>
    /// <exception cref="ExternalEditorException">The editor is missing, unconfigured, or would not start.</exception>
    public async Task LaunchAsync(
        ExternalEditor editor,
        ExternalEditorTarget target,
        EditorCommands custom,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(custom);

        if (!RepositoryPaths.IsRepositoryRelative(target.Path, out var relativePath))
        {
            throw new ExternalEditorException(
                EditorFailures.LaunchFailed,
                $"'{target.Path}' is not a repository-relative path.");
        }

        var previousRelative = (string?)null;
        if (target.PreviousPath is { Length: > 0 } previous &&
            RepositoryPaths.IsRepositoryRelative(previous, out var resolvedPrevious))
        {
            previousRelative = resolvedPrevious;
        }

        var commit = await git.GetHeadCommitAsync(target.RepositoryPath, cancellationToken).ConfigureAwait(false);

        // The committed side is read from the path the file had before it moved, which is the whole
        // reason previousPath crosses the bridge at all.
        var committed = await extractor
            .ExtractAsync(
                target.RepositoryPath,
                previousRelative ?? relativePath,
                commit ?? "head",
                cancellationToken)
            .ConfigureAwait(false);

        var working = RepositoryPaths.ToAbsolute(target.RepositoryPath, relativePath);
        var workingExists = File.Exists(working);

        var (executable, arguments) = (committed, workingExists) switch
        {
            // Both sides exist: a comparison, which is what the reviewer asked for.
            (not null, true) => await DiffCommandAsync(editor, committed, working, custom, cancellationToken)
                .ConfigureAwait(false),

            // Added, untracked, or committed-side-unavailable (binary, too large): the file itself.
            (null, true) => await OpenCommandAsync(editor, working, target.Line, custom, cancellationToken)
                .ConfigureAwait(false),

            // Deleted: there is nothing on disk, so the extracted committed copy is the only thing
            // there is to look at.
            (not null, false) => await OpenCommandAsync(editor, committed, target.Line, custom, cancellationToken)
                .ConfigureAwait(false),

            _ => throw new ExternalEditorException(
                EditorFailures.LaunchFailed,
                $"Neither side of '{relativePath}' could be opened."),
        };

        Start(executable, arguments);
        LaunchedEditor(logger, editor.ToString(), relativePath);
    }

    private async ValueTask<(string Executable, IReadOnlyList<string> Arguments)> DiffCommandAsync(
        ExternalEditor editor,
        string left,
        string right,
        EditorCommands custom,
        CancellationToken cancellationToken)
    {
        if (editor == ExternalEditor.Custom)
        {
            return Template(custom.Diff, [("{left}", left), ("{right}", right)]);
        }

        var executable = await Require(editor, cancellationToken).ConfigureAwait(false);

        return editor == ExternalEditor.VisualStudio
            ? (executable, ["/diff", left, right])
            : (executable, ["--diff", left, right]);
    }

    private async ValueTask<(string Executable, IReadOnlyList<string> Arguments)> OpenCommandAsync(
        ExternalEditor editor,
        string file,
        int line,
        EditorCommands custom,
        CancellationToken cancellationToken)
    {
        // Zero means "the node is the whole file", and line 1 is where an editor opens a file anyway.
        var target = Math.Max(line, 1);

        if (editor == ExternalEditor.Custom)
        {
            return Template(
                custom.Open,
                [("{file}", file), ("{line}", target.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        }

        var executable = await Require(editor, cancellationToken).ConfigureAwait(false);

        // devenv has no "go to line" switch that works from a cold start; /edit at least reuses a
        // running instance instead of opening a second one.
        return editor == ExternalEditor.VisualStudio
            ? (executable, ["/edit", file])
            : (executable, ["--goto", $"{file}:{target}"]);
    }

    private async ValueTask<string> Require(ExternalEditor editor, CancellationToken cancellationToken)
    {
        var executable = await locator.FindAsync(editor, cancellationToken).ConfigureAwait(false);

        return executable ?? throw new ExternalEditorException(
            EditorFailures.NotFound,
            $"{editor} was not found on this machine.");
    }

    /// <summary>
    /// Splits the user's command template on whitespace and substitutes the placeholders afterwards,
    /// so a path containing a space cannot split itself into two arguments.
    /// </summary>
    private static (string Executable, IReadOnlyList<string> Arguments) Template(
        string template,
        IReadOnlyList<(string Token, string Value)> substitutions)
    {
        var words = template.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            throw new ExternalEditorException(
                EditorFailures.NotConfigured,
                "No custom editor command is configured.");
        }

        var arguments = new List<string>(words.Length - 1);

        foreach (var word in words.Skip(1))
        {
            var argument = word;

            foreach (var (token, value) in substitutions)
            {
                argument = argument.Replace(token, value, StringComparison.Ordinal);
            }

            arguments.Add(argument);
        }

        return (words[0], arguments);
    }

    private static void Start(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);

            // A child holding our stdin is the bug that made every MCP call block for a full
            // timeout. An editor has even less business with it.
            process?.StandardInput.Close();
        }
        catch (Win32Exception ex)
        {
            // What Windows raises for a missing or unrunnable executable, and what a renamed `code`
            // arrives as. Requirement 3's "handle VS Code not being installed" is this line.
            throw new ExternalEditorException(EditorFailures.NotFound, ex.Message);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ExternalEditorException(EditorFailures.NotFound, ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new ExternalEditorException(EditorFailures.LaunchFailed, ex.Message);
        }
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Opened {Path} in {Editor}.")]
    private static partial void LaunchedEditor(ILogger logger, string editor, string path);
}

/// <summary>Which file to open, and where in it.</summary>
public sealed record ExternalEditorTarget
{
    public required string RepositoryPath { get; init; }

    /// <summary>Repository-relative path, as git spells it.</summary>
    public required string Path { get; init; }

    /// <summary>Where the file was before it moved, for a rename. Null otherwise.</summary>
    public string? PreviousPath { get; init; }

    /// <summary>Line to land on, counting from 1. Zero when the node covers the whole file.</summary>
    public int Line { get; init; }
}

/// <summary>The two command templates the user configured. Either may be empty.</summary>
public sealed record EditorCommands(string Diff, string Open)
{
    public static EditorCommands None { get; } = new(string.Empty, string.Empty);
}
