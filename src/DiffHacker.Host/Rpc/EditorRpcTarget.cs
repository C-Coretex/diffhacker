using DiffHacker.Contracts;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Settings;
using DiffHacker.Host.Editor;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// The external-editor surface: which editors this machine has, what the user configured for one it
/// does not, and open a file in one of them.
/// <para>
/// <c>describe</c> and <c>save</c> both return the whole <see cref="EditorSettings"/>, the convention
/// every other settings surface here follows. <c>open</c> returns nothing: it either started an editor
/// or it threw a code the interface can phrase, and there is no third answer worth a payload.
/// </para>
/// <para>
/// The renderer never sends a command line. It names an editor and a file, and the host decides what
/// to run — so a page with no filesystem access (§0.2.13) cannot become one that runs arbitrary
/// processes by composing a string.
/// </para>
/// </summary>
public sealed class EditorRpcTarget(
    ExternalEditorLocator locator,
    ExternalEditorLauncher launcher,
    IAppSettingStore settings,
    ILogger<EditorRpcTarget> logger)
{
    private const string DiffCommandKey = "editor_custom_diff_command";
    private const string OpenCommandKey = "editor_custom_open_command";

    [JsonRpcMethod("editor.describe")]
    public async Task<EditorSettings> DescribeAsync(CancellationToken cancellationToken)
    {
        var custom = await ReadCommandsAsync(cancellationToken).ConfigureAwait(false);

        return new EditorSettings(
            customDiffCommand: custom.Diff,
            customOpenCommand: custom.Open,
            visualStudioAvailable:
                await locator.FindAsync(ExternalEditor.VisualStudio, cancellationToken).ConfigureAwait(false) is not null,
            vsCodeAvailable:
                await locator.FindAsync(ExternalEditor.VsCode, cancellationToken).ConfigureAwait(false) is not null);
    }

    [JsonRpcMethod("editor.save")]
    public async Task<EditorSettings> SaveAsync(
        SaveEditorSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A template with no placeholder in it would launch the editor on nothing and look like the
        // editor was at fault. Rejected before it is stored, when the user is still looking at the
        // field they typed it into.
        Require(request.CustomDiffCommand, "editor_invalid_diff_command", "{left}", "{right}");
        Require(request.CustomOpenCommand, "editor_invalid_open_command", "{file}");

        await settings.SetAsync(DiffCommandKey, request.CustomDiffCommand.Trim(), cancellationToken)
            .ConfigureAwait(false);
        await settings.SetAsync(OpenCommandKey, request.CustomOpenCommand.Trim(), cancellationToken)
            .ConfigureAwait(false);

        return await DescribeAsync(cancellationToken).ConfigureAwait(false);
    }

    [JsonRpcMethod("editor.open")]
    public async Task OpenAsync(OpenInEditorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var custom = await ReadCommandsAsync(cancellationToken).ConfigureAwait(false);

        var target = new ExternalEditorTarget
        {
            RepositoryPath = request.RepositoryPath,
            Path = request.Path,
            PreviousPath = request.PreviousPath,
            Line = request.Line,
        };

        try
        {
            await launcher
                .LaunchAsync(FromWire(request.Editor), target, custom, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ExternalEditorException ex)
        {
            // The message carries the operating system's own words about a missing executable. That
            // belongs in log.txt; the interface resolves the code through its own catalogue (§0.6).
            logger.LogWarning(ex, "Opening {Path} in {Editor} failed", request.Path, request.Editor);

            throw RpcErrors.Failure(
                ex.Failure,
                ex.Message,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = request.Path });
        }
        catch (GitClientException ex)
        {
            logger.LogWarning(ex, "The committed side of {Path} could not be read", request.Path);

            throw RpcErrors.Failure(
                ex.Failure is GitClientFailure.GitUnavailable ? "git_not_found" : "changeset_git_failed",
                ex.Message,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = request.RepositoryPath });
        }
    }

    private async ValueTask<EditorCommands> ReadCommandsAsync(CancellationToken cancellationToken)
    {
        var diff = await settings.GetAsync(DiffCommandKey, cancellationToken).ConfigureAwait(false);
        var open = await settings.GetAsync(OpenCommandKey, cancellationToken).ConfigureAwait(false);

        return new EditorCommands(diff ?? string.Empty, open ?? string.Empty);
    }

    /// <summary>An empty template is "not configured", which is allowed. A malformed one is not.</summary>
    private static void Require(string template, string code, params string[] placeholders)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        foreach (var placeholder in placeholders)
        {
            if (!template.Contains(placeholder, StringComparison.Ordinal))
            {
                throw RpcErrors.Failure(
                    code,
                    $"The command '{template}' does not contain {placeholder}.",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["placeholder"] = placeholder });
            }
        }
    }

    private static ExternalEditor FromWire(OpenInEditorRequestEditor editor) => editor switch
    {
        OpenInEditorRequestEditor.Vscode => ExternalEditor.VsCode,
        OpenInEditorRequestEditor.Visual_studio => ExternalEditor.VisualStudio,
        _ => ExternalEditor.Custom,
    };
}
