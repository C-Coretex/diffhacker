using DiffHacker.Git;
using Microsoft.Extensions.Logging.Abstractions;
using DiffHacker.TestSupport;

namespace DiffHacker.Git.Tests;

/// <summary>
/// CLAUDE.md §0.2.12 makes DiffHacker read-only with respect to the repositories it reviews.
/// The allowlist is what enforces that, so it is worth a test rather than a comment.
/// </summary>
public sealed class GitProcessRunnerTests
{
    private readonly GitProcessRunner _runner = new(NullLogger<GitProcessRunner>.Instance);

    [Theory]
    [InlineData("commit")]
    [InlineData("checkout")]
    [InlineData("add")]
    [InlineData("reset")]
    [InlineData("push")]
    [InlineData("clean")]
    [InlineData("stash")]
    [InlineData("merge")]
    [InlineData("rebase")]
    [InlineData("gc")]
    [InlineData("mv")]
    [InlineData("rm")]
    [InlineData("apply")]
    [InlineData("submodule")]
    [InlineData("hash-object")]
    [InlineData("update-index")]
    public async Task A_mutating_subcommand_is_rejected_before_a_process_starts(string subcommand)
    {
        var thrown = await Should.ThrowAsync<ArgumentException>(
            () => _runner.RunAsync(subcommand, [], null, TestContext.Current.CancellationToken));

        // The allowlist must explain itself: a bare "not permitted" invites someone to widen it.
        thrown.Message.ShouldContain("read-only git allowlist");
    }

    [Fact]
    public async Task The_streaming_entry_point_enforces_the_same_allowlist()
    {
        // Iteration 3 added a second way into the runner. A door that skipped the check would
        // make the whole allowlist decorative.
        var thrown = await Should.ThrowAsync<ArgumentException>(
            () => _runner.RunStreamingAsync(
                "commit",
                [],
                null,
                static (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain("read-only git allowlist");
    }

    [Fact]
    public void The_allowlist_contains_only_read_only_subcommands()
    {
        // A denylist would be wrong the moment git grows a subcommand. This asserts the shape
        // of the rule, so widening it stays a deliberate act — which is why this test failed
        // when Iteration 5 added grep, and why it should fail for the next one too.
        //
        // Iteration 3 added diff, ls-files and cat-file. Iteration 5 added grep, for the
        // toolbox's repository-wide search: it has no mutating form at all, and the alternatives
        // were an external search binary — the command execution the toolbox is forbidden — or
        // reimplementing .gitignore traversal.
        //
        // Note what is still absent: submodule, whose read-only status query cannot be granted
        // without also granting `submodule update`, and hash-object, which writes as soon as
        // anyone passes -w.
        GitProcessRunner.PermittedSubcommands.ShouldBe(
            ["version", "rev-parse", "diff", "ls-files", "cat-file", "grep"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task The_streaming_entry_point_hands_over_raw_bytes()
    {
        // Everything downstream reads NUL-delimited records and blob content, both of which a
        // UTF-8 StreamReader would corrupt. This asserts the stream is the process's own.
        var captured = new MemoryStream();

        var outcome = await _runner.RunStreamingAsync(
            "version",
            [],
            null,
            async (stream, token) => await stream.CopyToAsync(captured, token),
            TestContext.Current.CancellationToken);

        outcome.Succeeded.ShouldBeTrue();
        System.Text.Encoding.UTF8.GetString(captured.ToArray()).ShouldContain("git version");
    }

    [Fact]
    public async Task A_permitted_subcommand_runs()
    {
        var result = await _runner.RunAsync("version", [], null, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        result.StandardOutput.ShouldContain("git version");
    }

    [Fact]
    public async Task An_unlaunchable_git_is_reported_rather_than_thrown()
    {
        // Requirement 6's failure mode. Pointing at a name that cannot exist reproduces "git is
        // not on PATH" without touching the environment of the machine running the tests.
        var missing = new GitProcessRunner(
            NullLogger<GitProcessRunner>.Instance,
            "diffhacker-no-such-git-" + Guid.NewGuid().ToString("n"));

        var result = await missing.RunAsync("version", [], null, TestContext.Current.CancellationToken);

        result.CouldNotRun.ShouldBeTrue();
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_git_surfaces_as_unavailable_not_as_a_crash()
    {
        var missing = new GitProcessRunner(
            NullLogger<GitProcessRunner>.Instance,
            "diffhacker-no-such-git-" + Guid.NewGuid().ToString("n"));

        var environment = new GitEnvironment(missing, NullLogger<GitEnvironment>.Instance);

        var availability = await environment.ProbeAsync(TestContext.Current.CancellationToken);

        availability.Available.ShouldBeFalse();
        availability.Version.ShouldBeNull();
    }

    [Fact]
    public async Task Without_git_a_repository_is_rejected_as_git_unavailable()
    {
        var missing = new GitProcessRunner(
            NullLogger<GitProcessRunner>.Instance,
            "diffhacker-no-such-git-" + Guid.NewGuid().ToString("n"));

        var locator = new RepositoryLocator(
            missing,
            new GitEnvironment(missing, NullLogger<GitEnvironment>.Instance),
            NullLogger<RepositoryLocator>.Instance);

        using var fixture = FixtureRepository.CreateWithCommit();

        var resolution = await locator.ResolveAsync(fixture.Root, TestContext.Current.CancellationToken);

        // Not "that is not a repository" — it demonstrably is one. The app cannot tell without
        // git, and says so.
        resolution.Rejection.ShouldBe(DiffHacker.Core.Repositories.RepositoryRejection.GitUnavailable);
    }
}
