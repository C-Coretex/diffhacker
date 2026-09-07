using DiffHacker.Core.Knowledge;

namespace DiffHacker.Core.Settings;

/// <summary>
/// Persistence for what is known about a repository.
/// <para>
/// The two save methods are separate on purpose, and it is the whole reason this interface is not
/// one <c>SaveAsync</c>. <see cref="SaveGeneratedAsync"/> writes only the model's half;
/// <see cref="SaveUserSectionsAsync"/> writes only the user's. Neither can reach the other's
/// columns, so "manual edits survive regeneration" holds even if a caller gets it wrong.
/// </para>
/// </summary>
public interface IProjectProfileStore
{
    /// <summary>
    /// What is stored for <paramref name="repositoryPath"/>, or null when nothing is — no
    /// generated document and no user text either.
    /// </summary>
    ValueTask<ProjectProfile?> GetAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the generated document and its provenance. The user's notes, instructions,
    /// excluded globs and budget are left exactly as they were.
    /// </summary>
    ValueTask SaveGeneratedAsync(
        string repositoryPath,
        ProjectProfileDocument document,
        ProfileProvenance provenance,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the generated document after the user has edited it, keeping the provenance —
    /// the document still came from that run, whatever has been typed into it since.
    /// </summary>
    ValueTask SaveEditedDocumentAsync(
        string repositoryPath,
        ProjectProfileDocument document,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces everything the user authored. Works before a profile has ever been generated,
    /// because custom instructions are useful on their own.
    /// </summary>
    ValueTask SaveUserSectionsAsync(
        string repositoryPath,
        string userNotes,
        string customInstructions,
        IReadOnlyList<string> customExcludedGlobs,
        int? characterBudget,
        CancellationToken cancellationToken);

    /// <summary>Forgets the whole profile, the user's sections included.</summary>
    ValueTask DeleteAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>Keeps a finished run, pruning the oldest so the history stays bounded.</summary>
    ValueTask RecordRunAsync(ProfileRun run, CancellationToken cancellationToken);

    /// <summary>Runs for one repository, most recent first.</summary>
    ValueTask<IReadOnlyList<ProfileRun>> ListRunsAsync(
        string repositoryPath,
        CancellationToken cancellationToken);
}
