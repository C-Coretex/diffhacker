using DiffHacker.Contracts;
using DiffHacker.Core.Knowledge;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Domain to wire and back for the profile contracts.
/// <para>
/// The generated records duplicate the module and entry-point shapes across three schema files,
/// because a schema cannot reference a definition in another file and the code generator cannot
/// nest one definition inside another. <c>ProjectProfileAgreementTests</c> is what stops the three
/// drifting; this is where the duplication is paid for, once.
/// </para>
/// </summary>
internal static class ProfileWire
{
    public static ProfileState ToWire(ProjectProfile profile, ProfileDrift drift)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var document = profile.Document;

        return new ProfileState(
            architecture: document?.Architecture,
            characterBudget: profile.EffectiveCharacterBudget,
            characterCount: profile.Provenance?.DocumentCharacters,
            customExcludedGlobs: profile.CustomExcludedGlobs,
            customInstructions: profile.CustomInstructions,
            documentationSources: document?.DocumentationSources ?? [],
            driftCommitReachable: drift.CommitReachable,
            driftFilesChanged: drift.FilesChanged,
            driftSubstantial: drift.IsSubstantial,
            driftTrackedFiles: drift.TrackedFiles,
            effectiveExcludedGlobs: profile.EffectiveExcludedGlobs,
            entryPoints: [.. (document?.EntryPoints ?? []).Select(static point =>
                new ProfileEntryPointInfo(path: point.Path, purpose: point.Purpose))],
            generatedAtUtc: profile.Provenance?.GeneratedAtUtc,
            generatedByModel: profile.Provenance?.Model,
            generatedByProvider: profile.Provenance?.ProviderDisplayName,
            generatedFromCommit: profile.Provenance?.CommitSha,
            hasProfile: document is not null,
            layering: document?.Layering,
            modules: [.. (document?.Modules ?? []).Select(static module => new ProfileModuleInfo(
                name: module.Name,
                path: module.Path,
                relatedModules: module.RelatedModules,
                summary: module.Summary))],
            patterns: document?.Patterns,
            purpose: document?.Purpose,
            repositoryPath: profile.RepositoryPath,
            testLayout: document?.TestLayout,
            userNotes: profile.UserNotes,
            userSectionCharacters: ProfileTextRenderer.MeasureUserSections(profile));
    }

    /// <remarks>
    /// The domain document, not the generated contract of the same name: that one is the shape the
    /// model answers in, this is the shape the application stores.
    /// </remarks>
    public static Core.Knowledge.ProjectProfileDocument FromWire(SaveProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new Core.Knowledge.ProjectProfileDocument
        {
            Purpose = request.Purpose,
            Architecture = request.Architecture,
            Modules = [.. request.Modules.Select(static module => new ProjectModule
            {
                Name = module.Name,
                Path = module.Path,
                Summary = module.Summary,
                RelatedModules = module.RelatedModules,
            })],
            Layering = request.Layering,
            Patterns = request.Patterns,
            EntryPoints = [.. request.EntryPoints.Select(static point => new ProjectEntryPoint
            {
                Path = point.Path,
                Purpose = point.Purpose,
            })],
            TestLayout = request.TestLayout,
            DocumentationSources = request.DocumentationSources,
        };
    }

    public static DocumentationTarget FromWire(DocumentationRequestTarget target) => target switch
    {
        DocumentationRequestTarget.Docs_directory => DocumentationTarget.DocsDirectory,
        _ => DocumentationTarget.RepositoryRoot,
    };

    public static DocumentationTarget FromWire(DocumentationExportRequestTarget target) => target switch
    {
        DocumentationExportRequestTarget.Docs_directory => DocumentationTarget.DocsDirectory,
        _ => DocumentationTarget.RepositoryRoot,
    };

    public static DocumentationPreviewTarget ToWire(DocumentationTarget target) => target switch
    {
        DocumentationTarget.DocsDirectory => DocumentationPreviewTarget.Docs_directory,
        _ => DocumentationPreviewTarget.Repository_root,
    };
}
