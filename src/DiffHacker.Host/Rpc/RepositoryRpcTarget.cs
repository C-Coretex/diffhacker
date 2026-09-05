using DiffHacker.Contracts;
using DiffHacker.Core.Repositories;
using DiffHacker.Core.Settings;
using DiffHacker.Host.Shell;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Choosing a repository: the native picker, validation, and the recent list.
/// </summary>
public sealed class RepositoryRpcTarget(
    IAppShell shell,
    IRepositoryLocator locator,
    IRecentRepositoryStore recents,
    ILogger<RepositoryRpcTarget> logger)
{
    [JsonRpcMethod("repository.browse")]
    public async Task<BrowseFolderResult> BrowseAsync(BrowseFolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var chosen = await shell
                .ShowFolderPickerAsync(request.Title, request.InitialDirectory, cancellationToken)
                .ConfigureAwait(false);

            // Cancelling is an ordinary outcome, not an error: the renderer needs to tell the
            // two apart so it does not show a failure for a dismissed dialog.
            return new BrowseFolderResult(cancelled: chosen is null, path: chosen);
        }
#pragma warning disable CA1031 // Any picker failure is reported to the user, not propagated as a crash.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "The native folder picker could not be opened");
            throw RpcErrors.Failure("folder_picker_unavailable", "The native folder picker failed: " + ex.Message);
        }
    }

    [JsonRpcMethod("repository.open")]
    public async Task<OpenRepositoryResult> OpenAsync(OpenRepositoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            throw RpcErrors.Failure("repository_not_found", "repository.open was called with an empty path.");
        }

        var resolution = await locator.ResolveAsync(request.Path, cancellationToken).ConfigureAwait(false);

        if (resolution.Rejection is not RepositoryRejection.None || resolution.Repository is null)
        {
            throw Reject(resolution.Rejection, request.Path);
        }

        var repository = resolution.Repository;
        await recents.RememberAsync(repository.Path, repository.Name, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Opened repository {Name} at {Path} (normalised from a subdirectory: {Normalised})",
            repository.Name,
            repository.Path,
            resolution.NormalizedFromSubdirectory);

        return new OpenRepositoryResult(
            normalizedFromSubdirectory: resolution.NormalizedFromSubdirectory,
            repository: new RepositoryInfo(
                hasCommits: repository.HasCommits,
                isLinkedWorktree: repository.IsLinkedWorktree,
                name: repository.Name,
                path: repository.Path));
    }

    [JsonRpcMethod("repository.listRecent")]
    public async Task<RecentRepositoryList> ListRecentAsync(CancellationToken cancellationToken)
    {
        var entries = await recents.ListAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<Contracts.RecentRepository>(entries.Count);
        foreach (var entry in entries)
        {
            // A repository that has been deleted or moved stays in the list, marked, so the
            // user can forget it deliberately rather than wondering where it went.
            var available = await locator.IsStillAvailableAsync(entry.Path, cancellationToken).ConfigureAwait(false);

            results.Add(new Contracts.RecentRepository(
                available: available,
                lastOpenedUtc: entry.LastOpenedUtc,
                name: entry.Name,
                path: entry.Path));
        }

        return new RecentRepositoryList(results.AsReadOnly());
    }

    [JsonRpcMethod("repository.forgetRecent")]
    public async Task ForgetRecentAsync(ForgetRecentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await recents.ForgetAsync(request.Path, cancellationToken).ConfigureAwait(false);
    }

    private static LocalRpcException Reject(RepositoryRejection rejection, string path)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = path };

        return rejection switch
        {
            RepositoryRejection.PathNotFound => RpcErrors.Failure(
                "repository_not_found", $"No directory exists at '{path}'.", args),

            RepositoryRejection.NotARepository => RpcErrors.Failure(
                "repository_not_a_git_repository", $"'{path}' is not inside a git working tree.", args),

            RepositoryRejection.BareRepository => RpcErrors.Failure(
                "repository_is_bare",
                $"'{path}' is a bare repository; it has no working tree to review.",
                args),

            RepositoryRejection.AccessDenied => RpcErrors.Failure(
                "repository_access_denied", $"'{path}' could not be read.", args),

            RepositoryRejection.GitUnavailable => RpcErrors.Failure(
                "git_not_found", "git is not available on PATH."),

            _ => RpcErrors.Failure("repository_not_a_git_repository", $"'{path}' was rejected.", args),
        };
    }
}
