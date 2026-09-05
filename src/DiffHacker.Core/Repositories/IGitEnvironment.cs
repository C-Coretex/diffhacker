namespace DiffHacker.Core.Repositories;

/// <summary>
/// Whether a usable <c>git</c> exists on <c>PATH</c>. The application is non-functional
/// without one, so this is probed once and reported plainly rather than discovered as a
/// confusing failure later (Iteration 2, requirement 6).
/// </summary>
public interface IGitEnvironment
{
    ValueTask<GitAvailability> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>Result of looking for the git command line.</summary>
/// <param name="Available">True when git ran and reported a version.</param>
/// <param name="Version">The version string git reported, or null when it did not run.</param>
public readonly record struct GitAvailability(bool Available, string? Version)
{
    public static GitAvailability Missing => new(false, null);
}
