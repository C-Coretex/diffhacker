using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Tests;

/// <summary>
/// The path rules the git layer and the toolbox sandbox both depend on.
/// <para>
/// These used to be two private helpers in two projects. They are one thing now because a
/// sandbox whose idea of "inside the repository" differs from the layer beneath it is a sandbox
/// with a seam in it — and containment in particular has a trap that a plain
/// <c>StartsWith</c> falls straight into.
/// </para>
/// </summary>
public sealed class RepositoryPathsTests
{
    [Theory]
    [InlineData("src/app.ts", "src/app.ts")]
    [InlineData("src\\app.ts", "src/app.ts")]
    [InlineData("src//app.ts", "src/app.ts")]
    [InlineData("src/app/", "src/app")]
    public void Normalises_to_git_s_spelling(string input, string expected) =>
        RepositoryPaths.Normalise(input).ShouldBe(expected);

    [Theory]
    [InlineData("src/app.ts")]
    [InlineData("a")]
    [InlineData("deep/nested/path/file.md")]
    public void Accepts_a_plain_relative_path(string path)
    {
        RepositoryPaths.IsRepositoryRelative(path, out var normalised).ShouldBeTrue();
        normalised.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/etc/passwd")]
    [InlineData("../outside")]
    [InlineData("src/../../outside")]
    [InlineData("./src")]
    [InlineData("src/./app.ts")]
    [InlineData("C:/Windows/System32")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("\\\\server\\share")]
    public void Rejects_anything_that_is_not_one(string? path) =>
        RepositoryPaths.IsRepositoryRelative(path, out _).ShouldBeFalse();

    [Fact]
    public void Containment_compares_whole_segments()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));

        RepositoryPaths.Contains(root, root).ShouldBeTrue();
        RepositoryPaths.Contains(root, Path.Combine(root, "src", "app.ts")).ShouldBeTrue();

        // The trap: a naive StartsWith(root) says this is inside the repository. It is a
        // different directory that merely shares a prefix.
        RepositoryPaths.Contains(root, root + "-backup").ShouldBeFalse();
        RepositoryPaths.Contains(root, root + "2").ShouldBeFalse();
    }

    [Fact]
    public void Containment_ignores_a_trailing_separator()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));

        RepositoryPaths.Contains(root + Path.DirectorySeparatorChar, root).ShouldBeTrue();
        RepositoryPaths.PathsEqual(root, root + Path.DirectorySeparatorChar).ShouldBeTrue();
    }

    [Fact]
    public void Path_equality_follows_the_platform_s_case_rule()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Repo"));
        var shouted = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "REPO"));

        // Getting this backwards on Windows means a user's own repository reads as "outside
        // the repository" the moment the case of what they typed differs.
        RepositoryPaths.PathsEqual(root, shouted).ShouldBe(!OperatingSystem.IsLinux());
    }
}
