using DiffHacker.Core.Changes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Git.Tests;

/// <summary>Builds a <see cref="GitClient"/> wired to a real git, the way the app wires it.</summary>
internal static class GitClientFactory
{
    public static GitClient Create()
    {
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);

        return new GitClient(
            runner,
            new GitEnvironment(runner, NullLogger<GitEnvironment>.Instance),
            NullLogger<GitClient>.Instance);
    }

    public static Task<Changeset> LoadAsync(
        this GitClient client,
        FixtureRepository fixture,
        CancellationToken cancellationToken,
        bool includeUntracked = true) =>
        client.GetChangesetAsync(new ChangesetQuery(fixture.Root, includeUntracked), cancellationToken);

    public static ChangedFile File(this Changeset changeset, string path) =>
        changeset.Files.SingleOrDefault(file => string.Equals(file.Path, path, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"'{path}' is not in the changeset. It contains: {string.Join(", ", changeset.Files.Select(f => f.Path))}");
}
