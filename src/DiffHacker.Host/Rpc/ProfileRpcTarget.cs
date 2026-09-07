using DiffHacker.Contracts;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Knowledge;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Settings;
using DiffHacker.Host.Knowledge;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// The repository knowledge base: what is known about a repository, and the documentation that can
/// be derived from it.
/// <para>
/// Every method returns the whole <see cref="ProfileState"/> rather than a delta, so the renderer
/// replaces its state instead of merging — the same convention <c>providers.save</c> follows.
/// </para>
/// <para>
/// Two of these are unlike anything else in the application. <c>profile.generate</c> is the first
/// method that spends the user's money and can run for minutes, so it is called with an abort
/// signal and honours cancellation throughout. <c>profile.exportDocumentation</c> is the only
/// method that writes into the user's repository, and it refuses to do so without the token from
/// a preview of exactly the bytes it is about to write.
/// </para>
/// </summary>
public sealed class ProfileRpcTarget(
    IProjectProfileStore store,
    IProfileBuilder builder,
    IGitClient git,
    RepositoryDocumentationWriter writer,
    RunEventNotifier runEvents,
    TimeProvider clock,
    ILogger<ProfileRpcTarget> logger)
{
    [JsonRpcMethod("profile.get")]
    public async Task<ProfileState> GetAsync(ProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await StateAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
    }

    [JsonRpcMethod("profile.generate")]
    public async Task<ProfileState> GenerateAsync(ProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ProfileBuildResult result;

        try
        {
            result = await builder
                .BuildAsync(request.RepositoryPath, runEvents, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitClientException ex)
        {
            logger.LogError(ex, "Profiling {Path} could not read the repository", request.RepositoryPath);

            throw RpcErrors.Failure(
                ex.Failure is GitClientFailure.GitUnavailable ? "git_not_found" : "profile_repository_unreadable",
                ex.Message,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = request.RepositoryPath });
        }
        catch (LlmConfigurationException ex)
        {
            // A configuration mistake to fix in settings, not an outcome of a run. Its code is
            // already one the renderer translates.
            logger.LogWarning(ex, "The active provider cannot be used for profiling");
            throw RpcErrors.Failure(ex.FailureCode, ex.Message);
        }

        if (!result.Succeeded)
        {
            var args = new Dictionary<string, string>(StringComparer.Ordinal);

            if (result.ProviderMessage is { Length: > 0 } detail)
            {
                args["detail"] = detail;
            }

            throw RpcErrors.Failure(
                result.FailureCode ?? "profile_run_failed",
                result.ProviderMessage ?? "The profile run did not complete.",
                args.Count == 0 ? null : args);
        }

        return await StateAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
    }

    [JsonRpcMethod("profile.saveDocument")]
    public async Task<ProfileState> SaveDocumentAsync(
        SaveProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await store.GetAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);

        if (existing?.Document is null)
        {
            throw RpcErrors.Failure(
                "profile_not_found",
                $"There is no generated profile for '{request.RepositoryPath}' to edit.");
        }

        await store
            .SaveEditedDocumentAsync(request.RepositoryPath, ProfileWire.FromWire(request), cancellationToken)
            .ConfigureAwait(false);

        return await StateAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
    }

    [JsonRpcMethod("profile.saveNotes")]
    public async Task<ProfileState> SaveNotesAsync(
        SaveProfileNotesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await store.SaveUserSectionsAsync(
            request.RepositoryPath,
            request.UserNotes,
            request.CustomInstructions,
            request.CustomExcludedGlobs,
            request.CharacterBudget is { } budget ? ProfileBudget.Clamp(budget) : null,
            cancellationToken).ConfigureAwait(false);

        return await StateAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
    }

    [JsonRpcMethod("profile.delete")]
    public async Task<ProfileState> DeleteAsync(ProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await store.DeleteAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
        return await StateAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
    }

    [JsonRpcMethod("profile.previewDocumentation")]
    public async Task<DocumentationPreview> PreviewDocumentationAsync(
        DocumentationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = ProfileWire.FromWire(request.Target);
        var documents = await RenderAsync(request.RepositoryPath, target, cancellationToken).ConfigureAwait(false);

        var files = new List<DocumentationFile>(documents.Count);

        foreach (var document in documents)
        {
            var existing = RepositoryDocumentationWriter.Existing(request.RepositoryPath, document.RelativePath);

            files.Add(new DocumentationFile(
                content: document.Content,
                exists: existing.Exists,
                existingIsUnreadable: existing.Exists && existing.Text is null,
                relativePath: document.RelativePath,
                unifiedDiff: existing.Text is { } text
                    ? UnifiedDiff.Compute(text, document.Content, document.RelativePath)
                    : null));
        }

        return new DocumentationPreview(
            files: files,
            previewToken: RepositoryDocumentationWriter.TokenFor(target, documents),
            repositoryPath: request.RepositoryPath,
            target: ProfileWire.ToWire(target));
    }

    [JsonRpcMethod("profile.exportDocumentation")]
    public async Task<DocumentationExportResult> ExportDocumentationAsync(
        DocumentationExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = ProfileWire.FromWire(request.Target);
        var documents = await RenderAsync(request.RepositoryPath, target, cancellationToken).ConfigureAwait(false);

        // Recomputed from what is about to be written, not trusted from the request. A token that
        // no longer matches means the profile changed since the preview, and the user has approved
        // bytes that are not these.
        if (!string.Equals(
                RepositoryDocumentationWriter.TokenFor(target, documents),
                request.PreviewToken,
                StringComparison.Ordinal))
        {
            throw RpcErrors.Failure(
                "documentation_preview_required",
                "The documentation changed since it was previewed, so nothing was written.");
        }

        try
        {
            var overwritten = writer.Write(request.RepositoryPath, documents);

            return new DocumentationExportResult(
                overwrittenPaths: overwritten,
                repositoryPath: request.RepositoryPath,
                writtenPaths: [.. documents.Select(document => document.RelativePath)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Documentation could not be written into {Path}", request.RepositoryPath);

            throw RpcErrors.Failure(
                "documentation_write_failed",
                ex.Message,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = request.RepositoryPath });
        }
    }

    private async Task<IReadOnlyList<GeneratedDocument>> RenderAsync(
        string repositoryPath,
        DocumentationTarget target,
        CancellationToken cancellationToken)
    {
        var profile = await store.GetAsync(repositoryPath, cancellationToken).ConfigureAwait(false);

        if (profile?.Document is null)
        {
            throw RpcErrors.Failure(
                "documentation_no_profile",
                $"There is no generated profile for '{repositoryPath}', so there is nothing to document.");
        }

        return DocumentationRenderer.Render(profile, target);
    }

    /// <summary>
    /// The whole state, drift included. Every method ends here so the renderer sees one shape.
    /// </summary>
    private async Task<ProfileState> StateAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var profile = await store.GetAsync(repositoryPath, cancellationToken).ConfigureAwait(false)
            ?? ProjectProfile.Empty(repositoryPath, clock.GetUtcNow());

        return ProfileWire.ToWire(
            profile,
            await DriftAsync(profile, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// How far the repository has moved since the profile was generated.
    /// <para>
    /// Two git calls, and both are cheap enough to make on every <c>profile.get</c>. A failure to
    /// measure is not a failure to answer: drift is advisory, and a repository git cannot read
    /// right now will fail loudly on the next thing that actually needs it.
    /// </para>
    /// </summary>
    private async Task<ProfileDrift> DriftAsync(ProjectProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Provenance?.CommitSha is not { Length: > 0 } commit)
        {
            return ProfileDrift.Unknown;
        }

        try
        {
            var comparison = await git
                .CompareWithHeadAsync(profile.RepositoryPath, commit, cancellationToken)
                .ConfigureAwait(false);

            var tracked = await git
                .ListFilesAsync(new FileListQuery(profile.RepositoryPath), cancellationToken)
                .ConfigureAwait(false);

            return ProfileDriftCalculator.Evaluate(comparison.FilesChanged, tracked.Count, comparison.CommitReachable);
        }
        catch (GitClientException ex)
        {
            logger.LogDebug(ex, "Drift for {Path} could not be measured", profile.RepositoryPath);
            return ProfileDrift.Unknown;
        }
    }
}
