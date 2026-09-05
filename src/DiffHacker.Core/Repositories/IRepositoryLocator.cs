namespace DiffHacker.Core.Repositories;

/// <summary>
/// Turns a path the user chose into a working tree the application will accept, or a specific
/// reason it will not.
/// </summary>
public interface IRepositoryLocator
{
    /// <summary>
    /// Resolves <paramref name="path"/> to the root of its working tree. A directory inside a
    /// repository resolves upwards; a bare repository is rejected because it has no working
    /// tree to review.
    /// </summary>
    ValueTask<RepositoryResolution> ResolveAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Cheap check used when listing recent repositories, where a full resolve per entry would
    /// mean one git process per row.
    /// </summary>
    ValueTask<bool> IsStillAvailableAsync(string path, CancellationToken cancellationToken);
}
