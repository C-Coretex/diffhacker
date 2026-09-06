using DiffHacker.Core.Repositories;
using DiffHacker.Git;
using Microsoft.Extensions.Logging.Abstractions;
using DiffHacker.TestSupport;

namespace DiffHacker.Git.Tests;

/// <summary>
/// The repository-acceptance rules settled during Iteration 2 planning, each against a real
/// repository rather than a mock.
/// </summary>
public sealed class RepositoryLocatorTests
{
    private readonly RepositoryLocator _locator;

    public RepositoryLocatorTests()
    {
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);
        var environment = new GitEnvironment(runner, NullLogger<GitEnvironment>.Instance);
        _locator = new RepositoryLocator(runner, environment, NullLogger<RepositoryLocator>.Instance);
    }

    [Fact]
    public async Task A_repository_root_is_accepted()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        var resolution = await _locator.ResolveAsync(fixture.Root, TestContext.Current.CancellationToken);

        resolution.Rejection.ShouldBe(RepositoryRejection.None);
        resolution.NormalizedFromSubdirectory.ShouldBeFalse();
        resolution.Repository.ShouldNotBeNull();
        resolution.Repository!.HasCommits.ShouldBeTrue();
        resolution.Repository.IsLinkedWorktree.ShouldBeFalse();
        resolution.Repository.Name.ShouldBe(Path.GetFileName(fixture.Root));
    }

    [Fact]
    public async Task A_subdirectory_resolves_upwards_to_the_worktree_root()
    {
        using var fixture = FixtureRepository.CreateWithCommit();
        var nested = fixture.CreateSubdirectory(Path.Combine("src", "deep", "nested"));

        var resolution = await _locator.ResolveAsync(nested, TestContext.Current.CancellationToken);

        resolution.Rejection.ShouldBe(RepositoryRejection.None);
        resolution.Repository!.Path.ShouldBe(Path.GetFullPath(fixture.Root));

        resolution.NormalizedFromSubdirectory.ShouldBeTrue(
            "The interface tells the user their path changed rather than silently swapping it.");
    }

    [Fact]
    public async Task A_bare_repository_is_rejected_by_name()
    {
        using var fixture = FixtureRepository.CreateBare();

        var resolution = await _locator.ResolveAsync(fixture.Root, TestContext.Current.CancellationToken);

        resolution.Rejection.ShouldBe(
            RepositoryRejection.BareRepository,
            "A bare repository has no working tree, and working-tree-vs-HEAD is all this app reviews (§0.2.11).");
        resolution.Repository.ShouldBeNull();
    }

    [Fact]
    public async Task A_repository_with_no_commits_is_accepted_and_flagged()
    {
        using var fixture = FixtureRepository.CreateWithoutCommits();

        var resolution = await _locator.ResolveAsync(fixture.Root, TestContext.Current.CancellationToken);

        resolution.Rejection.ShouldBe(RepositoryRejection.None);
        resolution.Repository!.HasCommits.ShouldBeFalse(
            "There is no HEAD to compare against, and Iteration 3 needs to know that.");
    }

    [Fact]
    public async Task A_linked_worktree_is_accepted_and_identified()
    {
        using var fixture = FixtureRepository.CreateWithCommit();
        var worktree = fixture.AddLinkedWorktree("feature");

        try
        {
            var resolution = await _locator.ResolveAsync(worktree, TestContext.Current.CancellationToken);

            resolution.Rejection.ShouldBe(RepositoryRejection.None);
            resolution.Repository!.IsLinkedWorktree.ShouldBeTrue();
            resolution.Repository.Path.ShouldBe(Path.GetFullPath(worktree));
        }
        finally
        {
            if (Directory.Exists(worktree))
            {
                Directory.Delete(worktree, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_submodule_directory_is_accepted_as_its_own_repository()
    {
        using var inner = FixtureRepository.CreateWithCommit();
        using var outer = FixtureRepository.CreateWithCommit();

        var submodule = outer.AddSubmodule(inner, "vendor/inner");

        var resolution = await _locator.ResolveAsync(submodule, TestContext.Current.CancellationToken);

        resolution.Rejection.ShouldBe(RepositoryRejection.None);
        resolution.Repository!.Path.ShouldBe(
            Path.GetFullPath(submodule),
            "The user pointed at the submodule deliberately, so it is treated as its own working tree.");
    }

    [Fact]
    public async Task A_directory_that_is_not_a_repository_is_rejected()
    {
        using var fixture = FixtureRepository.CreateEmptyDirectory();

        var resolution = await _locator.ResolveAsync(fixture.Root, TestContext.Current.CancellationToken);

        resolution.Rejection.ShouldBe(RepositoryRejection.NotARepository);
    }

    [Fact]
    public async Task A_path_that_does_not_exist_is_reported_as_missing_not_as_a_non_repository()
    {
        var missing = Path.Combine(Path.GetTempPath(), "diffhacker-does-not-exist-" + Guid.NewGuid().ToString("n"));

        var resolution = await _locator.ResolveAsync(missing, TestContext.Current.CancellationToken);

        resolution.Rejection.ShouldBe(
            RepositoryRejection.PathNotFound,
            "'that folder is gone' and 'that folder is not a repository' are different problems for the user.");
    }

    [Fact]
    public async Task Availability_recognises_a_repository_and_a_plain_directory()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        using var plain = FixtureRepository.CreateEmptyDirectory();

        (await _locator.IsStillAvailableAsync(repository.Root, TestContext.Current.CancellationToken))
            .ShouldBeTrue();

        (await _locator.IsStillAvailableAsync(plain.Root, TestContext.Current.CancellationToken))
            .ShouldBeFalse();

        (await _locator.IsStillAvailableAsync(
            Path.Combine(Path.GetTempPath(), "diffhacker-gone-" + Guid.NewGuid().ToString("n")),
            TestContext.Current.CancellationToken)).ShouldBeFalse();
    }
}
