using DiffHacker.Contracts;
using DiffHacker.Core.Changes;
using DiffHacker.Host.Editor;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// The changeset: what changed, what one file's diff or content looks like, and saving an edit
/// made directly in the diff editor.
/// <para>
/// <c>changeset.load</c> returns metadata for every changed file and no content at all, so it
/// stays a bounded payload whether the change is ten files or fifteen hundred; the content and
/// diff methods fetch one file when the reviewer actually opens it, and <c>saveFileContent</c>
/// writes one file back when they edit it — §0.2.12's second write path, through
/// <see cref="RepositoryWorkingTreeWriter"/> rather than through <see cref="IGitClient"/>, which
/// stays read-only.
/// </para>
/// <para>
/// A clean working tree is a <b>result</b>, not an error: <c>isClean</c> on the response. So is
/// a file with no content on one side. Only genuine failure — git missing, git broken, the
/// repository unreadable, or a save that could not be trusted — throws.
/// </para>
/// </summary>
public sealed class ChangesetRpcTarget(
    IGitClient git,
    RepositoryWorkingTreeWriter writer,
    ILogger<ChangesetRpcTarget> logger)
{
    [JsonRpcMethod("changeset.load")]
    public async Task<ChangesetResult> LoadAsync(ChangesetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var changeset = await git
                .GetChangesetAsync(
                    new ChangesetQuery(request.RepositoryPath, request.IncludeUntracked),
                    cancellationToken)
                .ConfigureAwait(false);

            return ChangesetWire.ToWire(changeset);
        }
        catch (GitClientException ex)
        {
            throw Fail(ex, request.RepositoryPath);
        }
    }

    [JsonRpcMethod("changeset.fileDiff")]
    public async Task<FileDiffInfo> FileDiffAsync(FileDiffRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var diff = await git
                .GetFileDiffAsync(
                    new FileDiffQuery(
                        request.RepositoryPath,
                        request.Path,
                        request.PreviousPath,
                        request.Untracked),
                    cancellationToken)
                .ConfigureAwait(false);

            return ChangesetWire.ToWire(diff);
        }
        catch (GitClientException ex)
        {
            throw Fail(ex, request.RepositoryPath);
        }
    }

    [JsonRpcMethod("changeset.fileContent")]
    public async Task<FileContentInfo> FileContentAsync(
        FileContentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var content = await git
                .GetFileContentAsync(
                    new FileContentQuery(
                        request.RepositoryPath,
                        request.Path,
                        ChangesetWire.FromWire(request.Side)),
                    cancellationToken)
                .ConfigureAwait(false);

            return ChangesetWire.ToWire(content);
        }
        catch (GitClientException ex)
        {
            throw Fail(ex, request.RepositoryPath);
        }
    }

    [JsonRpcMethod("changeset.saveFileContent")]
    public async Task<SaveFileContentResult> SaveFileContentAsync(
        SaveFileContentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var sizeBytes = await writer
                .WriteAsync(
                    request.RepositoryPath,
                    request.Path,
                    request.Content,
                    request.ExpectedContent,
                    request.Encoding,
                    cancellationToken)
                .ConfigureAwait(false);

            return new SaveFileContentResult(sizeBytes);
        }
        catch (WorkingTreeWriteException ex)
        {
            // The exception carries the file's own path in its message, which is fine for
            // log.txt; the interface resolves the code through its own catalogue instead (§0.6).
            logger.LogWarning(ex, "The edit to {Path} in {Repository} could not be saved", request.Path, request.RepositoryPath);

            var args = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = request.Path };

            throw RpcErrors.Failure(ex.Failure, ex.Message, args);
        }
    }

    private LocalRpcException Fail(GitClientException exception, string repositoryPath)
    {
        // The exception message carries git's own stderr. That belongs in log.txt, never in the
        // interface, which resolves the code below through its own catalogue (§0.6).
        logger.LogError(exception, "The changeset for {Path} could not be produced", repositoryPath);

        var args = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = repositoryPath };

        return exception.Failure switch
        {
            GitClientFailure.GitUnavailable => RpcErrors.Failure(
                "git_not_found", exception.Message),

            GitClientFailure.RepositoryUnreadable => RpcErrors.Failure(
                "changeset_repository_unreadable", exception.Message, args),

            _ => RpcErrors.Failure("changeset_git_failed", exception.Message, args),
        };
    }
}
