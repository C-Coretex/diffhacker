using DiffHacker.Contracts;
using DiffHacker.Core.Changes;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// The changeset: what changed, and what one file's diff or content looks like.
/// <para>
/// Three methods, and the split between them is deliberate. <c>changeset.load</c> returns
/// metadata for every changed file and no content at all, so it stays a bounded payload whether
/// the change is ten files or fifteen hundred; the other two fetch one file when the reviewer
/// actually opens it.
/// </para>
/// <para>
/// A clean working tree is a <b>result</b>, not an error: <c>isClean</c> on the response. So is
/// a file with no content on one side. Only genuine failure — git missing, git broken, the
/// repository unreadable — throws.
/// </para>
/// </summary>
public sealed class ChangesetRpcTarget(IGitClient git, ILogger<ChangesetRpcTarget> logger)
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
