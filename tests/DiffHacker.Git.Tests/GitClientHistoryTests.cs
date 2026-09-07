using DiffHacker.TestSupport;

namespace DiffHacker.Git.Tests;

/// <summary>
/// The two history questions a stored profile asks: which commit it was taken from, and how far the
/// repository has moved since.
/// </summary>
public sealed class GitClientHistoryTests
{
    [Fact]
    public async Task The_head_commit_is_reported_as_git_spells_it()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        var git = GitClientFactory.Create();

        var head = await git.GetHeadCommitAsync(repository.Root, TestContext.Current.CancellationToken);

        head.ShouldBe(repository.HeadSha());
    }

    [Fact]
    public async Task A_repository_with_no_commits_has_no_head_rather_than_an_error()
    {
        // A first commit's worth of work is still a repository worth profiling.
        using var repository = FixtureRepository.CreateWithoutCommits();
        var git = GitClientFactory.Create();

        (await git.GetHeadCommitAsync(repository.Root, TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }

    [Fact]
    public async Task Comparing_head_with_itself_reports_no_drift()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        var git = GitClientFactory.Create();

        var comparison = await git.CompareWithHeadAsync(
            repository.Root, repository.HeadSha(), TestContext.Current.CancellationToken);

        comparison.CommitReachable.ShouldBeTrue();
        comparison.FilesChanged.ShouldBe(0);
    }

    [Fact]
    public async Task Files_committed_since_are_counted()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        var before = repository.HeadSha();

        repository.WriteFile("one.txt", "one\n");
        repository.WriteFile("two.txt", "two\n");
        repository.WriteFile("readme.md", "changed\n");
        repository.Stage(".");
        repository.Commit("three files");

        var git = GitClientFactory.Create();

        var comparison = await git.CompareWithHeadAsync(
            repository.Root, before, TestContext.Current.CancellationToken);

        comparison.CommitReachable.ShouldBeTrue();
        comparison.FilesChanged.ShouldBe(3);
    }

    [Fact]
    public async Task Uncommitted_work_is_not_drift()
    {
        // Drift is about the repository moving on, not about the review in progress. Counting the
        // working tree would make every profile look stale the moment someone started editing.
        using var repository = FixtureRepository.CreateWithCommit();
        var head = repository.HeadSha();

        repository.WriteFile("scratch.txt", "not committed\n");

        var git = GitClientFactory.Create();

        (await git.CompareWithHeadAsync(repository.Root, head, TestContext.Current.CancellationToken))
            .FilesChanged.ShouldBe(0);
    }

    [Fact]
    public async Task A_commit_that_is_not_in_this_repository_is_reported_as_unreachable()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        var git = GitClientFactory.Create();

        var comparison = await git.CompareWithHeadAsync(
            repository.Root,
            "0123456789abcdef0123456789abcdef01234567",
            TestContext.Current.CancellationToken);

        comparison.CommitReachable.ShouldBeFalse();
    }

    [Fact]
    public async Task Anything_that_is_not_an_object_name_is_refused_before_it_reaches_git()
    {
        // The value comes from our own database, but it reaches a command line either way, and a
        // stored string beginning with a dash would be read by git as an option.
        using var repository = FixtureRepository.CreateWithCommit();
        var git = GitClientFactory.Create();

        foreach (var hostile in new[] { "--help", "HEAD~1", "refs/heads/main", "abc" })
        {
            var comparison = await git.CompareWithHeadAsync(
                repository.Root, hostile, TestContext.Current.CancellationToken);

            comparison.CommitReachable.ShouldBeFalse(
                $"'{hostile}' is not a plain object name and must not reach git");
        }
    }
}
