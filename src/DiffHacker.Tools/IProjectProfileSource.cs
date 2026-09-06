namespace DiffHacker.Tools;

/// <summary>
/// Where <c>get_project_profile</c> reads from.
/// <para>
/// Iteration 6 builds the profile — standing knowledge about a repository, produced once and
/// reused. The tool ships in Iteration 5 anyway, with this seam behind it, because the tool
/// surface is what prompts and external agents are written against: adding a tool later means
/// changing a contract other things already depend on, whereas an honest "nothing stored yet" is
/// a real answer a model can act on.
/// </para>
/// </summary>
public interface IProjectProfileSource
{
    /// <summary>
    /// The stored profile for <paramref name="repositoryPath"/>, or null when there is none.
    /// </summary>
    ValueTask<string?> GetAsync(string repositoryPath, CancellationToken cancellationToken);
}

/// <summary>
/// The only implementation until Iteration 6: there is no profile.
/// </summary>
public sealed class NoProjectProfile : IProjectProfileSource
{
    public ValueTask<string?> GetAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        _ = repositoryPath;
        _ = cancellationToken;
        return ValueTask.FromResult<string?>(null);
    }
}
