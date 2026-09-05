using System.Diagnostics;

namespace DiffHacker.Git.Tests;

/// <summary>
/// Builds real git repositories in temporary directories.
/// <para>
/// Iteration 3's requirement 10 asks for exactly this, and the repository-validation rules
/// settled in Iteration 2 — subdirectories, bare repositories, linked worktrees, submodules,
/// repositories with no commits — cannot be tested any other way. A mocked git would only
/// prove the mock agrees with itself.
/// </para>
/// </summary>
internal sealed class FixtureRepository : IDisposable
{
    private FixtureRepository(string root) => Root = root;

    /// <summary>The temporary directory everything lives under.</summary>
    public string Root { get; }

    public static FixtureRepository CreateEmptyDirectory()
    {
        var root = Directory.CreateTempSubdirectory("diffhacker-git-").FullName;
        return new FixtureRepository(root);
    }

    /// <summary>An initialised repository with no commits at all.</summary>
    public static FixtureRepository CreateWithoutCommits()
    {
        var fixture = CreateEmptyDirectory();
        fixture.Git("init", "--initial-branch=main");
        fixture.ConfigureIdentity();
        return fixture;
    }

    /// <summary>An initialised repository with one commit.</summary>
    public static FixtureRepository CreateWithCommit()
    {
        var fixture = CreateWithoutCommits();
        fixture.WriteFile("readme.md", "fixture");
        fixture.Git("add", "readme.md");
        fixture.Commit("initial");
        return fixture;
    }

    public static FixtureRepository CreateBare()
    {
        var fixture = CreateEmptyDirectory();
        fixture.Git("init", "--bare", "--initial-branch=main");
        return fixture;
    }

    public string CreateSubdirectory(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public void WriteFile(string relativePath, string contents)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    public void Commit(string message) => Git("commit", "--no-gpg-sign", "-m", message);

    /// <summary>Adds a linked worktree and returns its path.</summary>
    public string AddLinkedWorktree(string name)
    {
        // Beside the repository rather than inside it, which is how people actually use them.
        var path = Path.Combine(Path.GetDirectoryName(Root)!, Path.GetFileName(Root) + "-" + name);
        Git("worktree", "add", "-b", name, path);
        return path;
    }

    /// <summary>Adds <paramref name="other"/> as a submodule and returns the submodule path.</summary>
    public string AddSubmodule(FixtureRepository other, string relativePath)
    {
        Git("-c", "protocol.file.allow=always", "submodule", "add", other.Root, relativePath);
        Commit("add submodule");
        return Path.Combine(Root, relativePath);
    }

    private void ConfigureIdentity()
    {
        // The machine running the tests may have no global identity, and commit would fail.
        Git("config", "user.email", "fixture@diffhacker.test");
        Git("config", "user.name", "DiffHacker Fixture");
        Git("config", "commit.gpgsign", "false");
    }

    /// <summary>
    /// Runs git directly rather than through <c>GitProcessRunner</c>: the fixture has to be
    /// able to write, and the runner exists precisely to make that impossible.
    /// </summary>
    public void Git(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(startInfo)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed with {process.ExitCode}: {stderr}");
        }
    }

    public void Dispose()
    {
        try
        {
            // git marks object files read-only, which blocks a plain recursive delete.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
