using DiffHacker.Core.Changes;

namespace DiffHacker.Git.Tests;

/// <summary>
/// The changeset is the artifact every later iteration consumes, so these are the tests that
/// decide whether anything built on top of it can be trusted.
/// <para>
/// One test per item in Iteration 3's "How to verify" list, against real repositories with real
/// commits, real renames and real untracked files.
/// </para>
/// </summary>
public sealed class GitClientChangesetTests
{
    private readonly GitClient _git = GitClientFactory.Create();

    [Fact]
    public async Task Staged_and_unstaged_edits_to_one_file_appear_once_with_combined_stats()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("app.cs", "one\ntwo\nthree\n");
        fixture.Stage("app.cs");
        fixture.Commit("add app");

        fixture.WriteFile("app.cs", "one\nSTAGED\nthree\n");
        fixture.Stage("app.cs");
        fixture.WriteFile("app.cs", "one\nSTAGED\nthree\nUNSTAGED\n");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        // The comparison is the working tree against HEAD, not the index against HEAD, so the
        // reviewer sees one file with everything they changed — which is what they are reviewing.
        changeset.Files.Count(file => file.Path == "app.cs")
            .ShouldBe(1, "Working tree vs HEAD is one comparison, so a file appears once (§0.2.11).");

        var app = changeset.File("app.cs");
        app.Status.ShouldBe(ChangeStatus.Modified);
        app.LinesAdded.ShouldBe(2);
        app.LinesRemoved.ShouldBe(1);
    }

    [Fact]
    public async Task An_untracked_file_is_included_by_default_and_a_gitignored_one_never_is()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteGitignore("secrets.env");
        fixture.Stage(".gitignore");
        fixture.Commit("ignore secrets");

        fixture.WriteFile("brand-new.ts", "export const value = 1;\n");
        fixture.WriteFile("secrets.env", "TOKEN=hunter2\n");

        var included = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        // An AI-generated change is mostly new files. Dropping them would violate §0.2.5 without
        // anything failing, which is exactly why this defaults to included.
        var untracked = included.File("brand-new.ts");
        untracked.Status.ShouldBe(ChangeStatus.Added);
        untracked.IsUntracked.ShouldBeTrue();
        untracked.LinesAdded.ShouldBe(1);
        untracked.LinesRemoved.ShouldBe(0);
        untracked.Language.ShouldBe("TypeScript");

        included.Files.ShouldNotContain(
            file => file.Path == "secrets.env",
            "A gitignored file is not part of the change in either mode.");

        var excluded = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken, includeUntracked: false);

        excluded.Files.ShouldNotContain(file => file.Path == "brand-new.ts");
        excluded.Files.ShouldNotContain(file => file.Path == "secrets.env");
        excluded.UntrackedIncluded.ShouldBeFalse();
    }

    [Fact]
    public async Task A_rename_is_a_rename_and_not_a_delete_plus_an_add()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("old-name.cs", string.Join('\n', Enumerable.Range(0, 40).Select(i => $"line {i}")) + "\n");
        fixture.Stage("old-name.cs");
        fixture.Commit("add");

        fixture.Rename("old-name.cs", "new-name.cs");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        var renamed = changeset.File("new-name.cs");
        renamed.Status.ShouldBe(ChangeStatus.Renamed);
        renamed.PreviousPath.ShouldBe("old-name.cs");

        changeset.Files.ShouldNotContain(
            file => file.Path == "old-name.cs",
            "A rename shown as a delete plus an add invents two nodes where the change has one.");
    }

    [Fact]
    public async Task A_binary_file_is_flagged_and_no_line_counts_are_invented_for_it()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteBinaryFile("logo.png");
        fixture.Stage("logo.png");
        fixture.Commit("add logo");

        fixture.WriteBinaryFile("logo.png", 1024);

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        var binary = changeset.File("logo.png");
        binary.IsBinary.ShouldBeTrue();
        binary.LinesAdded.ShouldBeNull("Git will not count a binary, and zero is a different claim from unknown.");
        binary.LinesRemoved.ShouldBeNull();
        binary.HunkCount.ShouldBeNull();

        changeset.Statistics.BinaryFiles.ShouldBe(1);
    }

    [Fact]
    public async Task A_repository_with_no_commits_compares_against_the_empty_tree()
    {
        using var fixture = FixtureRepository.CreateWithoutCommits();

        fixture.WriteFile("first.cs", "class First;\n");
        fixture.Stage("first.cs");
        fixture.WriteFile("second.cs", "class Second;\n");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        // There is no HEAD to compare against, so the empty tree stands in and a first commit's
        // worth of work still reviews as a changeset rather than failing.
        changeset.HasCommits.ShouldBeFalse();
        changeset.IsClean.ShouldBeFalse();

        changeset.File("first.cs").Status.ShouldBe(ChangeStatus.Added);
        changeset.File("first.cs").IsUntracked.ShouldBeFalse("It is staged, so git knows about it.");
        changeset.File("second.cs").IsUntracked.ShouldBeTrue();
    }

    [Fact]
    public async Task A_clean_working_tree_reports_clean_rather_than_an_empty_changeset()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        // Requirement 9. "Nothing to review" and "the analysis produced nothing" look identical
        // in an empty list, and only one of them is good news.
        changeset.IsClean.ShouldBeTrue();
        changeset.Files.ShouldBeEmpty();
        changeset.Statistics.TotalFiles.ShouldBe(0);
    }

    [Fact]
    public async Task A_file_with_no_trailing_newline_still_counts_its_last_line()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("tracked.txt", "alpha\nbravo");
        fixture.WriteFile("untracked.txt", "alpha\nbravo");
        fixture.Stage("tracked.txt");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        changeset.File("tracked.txt").LinesAdded.ShouldBe(2);

        // The untracked count is ours rather than git's, so it is the one that can disagree.
        changeset.File("untracked.txt").LinesAdded.ShouldBe(2, "A last line without a newline is still a line.");
    }

    [Fact]
    public async Task A_symlink_is_flagged_rather_than_followed()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("target.txt", "real content\n");
        fixture.Stage("target.txt");
        fixture.Commit("add target");

        if (!fixture.TryCreateSymlink("link.txt", "target.txt"))
        {
            Assert.Skip("This platform will not create symbolic links without elevation.");
        }

        fixture.Stage("link.txt");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        var link = changeset.File("link.txt");
        link.Status.ShouldBe(ChangeStatus.Added);
        link.IsSymlink.ShouldBeTrue("The mode bits say 120000, and a reviewer should be told this is a link.");
    }

    [Fact]
    public async Task A_dirty_submodule_is_one_flagged_entry_with_no_invented_line_counts()
    {
        using var inner = FixtureRepository.CreateWithCommit();
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.AddSubmodule(inner, "vendor/inner");

        // Move the submodule on, so the outer repository's recorded pointer is out of date.
        inner.WriteFile("second.md", "more\n");
        inner.Stage("second.md");
        inner.Commit("second");
        fixture.Git("-C", "vendor/inner", "fetch", "origin");
        fixture.Git("-C", "vendor/inner", "checkout", inner.HeadSha());

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        var submodule = changeset.File("vendor/inner");
        submodule.IsSubmodule.ShouldBeTrue();
        submodule.LinesAdded.ShouldBeNull(
            "Git's '-Subproject commit …' line is git's own synthesis, not a line of anyone's code.");
        submodule.Language.ShouldBeNull();
        submodule.SubmoduleToCommit.ShouldNotBeNull();

        changeset.Statistics.SubmoduleFiles.ShouldBe(1);
    }

    [Fact]
    public async Task A_file_is_attributed_to_the_nearest_manifest_above_it_not_to_the_repository_root()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("package.json", "{}\n");
        fixture.WriteFile("src/Web/package.json", "{}\n");
        fixture.Stage("package.json", "src/Web/package.json");
        fixture.Commit("manifests");

        fixture.WriteFile("src/Web/components/Button.tsx", "export const Button = () => null;\n");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        var component = changeset.File("src/Web/components/Button.tsx");

        component.Project.Name.ShouldBe("Web", "Nearest wins, or every file in a monorepo is one project.");
        component.Project.Manifest.ShouldBe("src/Web/package.json");
    }

    [Fact]
    public async Task Hunk_counts_are_attributed_per_file()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("wide.cs", string.Join('\n', Enumerable.Range(0, 60).Select(i => $"line {i}")) + "\n");
        fixture.WriteFile("narrow.cs", "only\n");
        fixture.Stage("wide.cs", "narrow.cs");
        fixture.Commit("baseline");

        // Two edits far enough apart that git cannot merge them into one hunk.
        var lines = Enumerable.Range(0, 60).Select(i => $"line {i}").ToArray();
        lines[2] = "changed near the top";
        lines[55] = "changed near the bottom";
        fixture.WriteFile("wide.cs", string.Join('\n', lines) + "\n");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        changeset.HunkCountsAvailable.ShouldBeTrue();
        changeset.File("wide.cs").HunkCount.ShouldBe(2);
    }

    [Fact]
    public async Task Statistics_are_counted_from_the_files_rather_than_estimated()
    {
        using var fixture = FixtureRepository.CreateWithCommit();

        fixture.WriteFile("keep.cs", "one\ntwo\n");
        fixture.WriteFile("drop.cs", "gone\n");
        fixture.Stage("keep.cs", "drop.cs");
        fixture.Commit("baseline");

        fixture.WriteFile("keep.cs", "one\ntwo\nthree\n");
        fixture.Delete("drop.cs");
        fixture.WriteFile("app.py", "print('hi')\n");

        var changeset = await _git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        changeset.Statistics.TotalFiles.ShouldBe(3);
        changeset.Statistics.ByStatus.Modified.ShouldBe(1);
        changeset.Statistics.ByStatus.Deleted.ShouldBe(1);
        changeset.Statistics.ByStatus.Added.ShouldBe(1);
        changeset.Statistics.UntrackedFiles.ShouldBe(1);
        changeset.Statistics.Languages.ShouldBe(["C#", "Python"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_path_that_is_not_a_repository_is_reported_rather_than_returned_empty()
    {
        using var fixture = FixtureRepository.CreateEmptyDirectory();

        var thrown = await Should.ThrowAsync<GitClientException>(
            () => _git.GetChangesetAsync(new ChangesetQuery(fixture.Root), TestContext.Current.CancellationToken));

        // An empty changeset from a non-repository would look exactly like a clean tree, and the
        // user would spend a while wondering where their changes went.
        thrown.Failure.ShouldBe(GitClientFailure.RepositoryUnreadable);
    }
}
