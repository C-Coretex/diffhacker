using System.Diagnostics;
using System.Text;

namespace DiffHacker.TestSupport;

/// <summary>
/// Builds real git repositories in temporary directories.
/// <para>
/// Iteration 3's requirement 10 asks for exactly this, and the repository-validation rules
/// settled in Iteration 2 — subdirectories, bare repositories, linked worktrees, submodules,
/// repositories with no commits — cannot be tested any other way. A mocked git would only
/// prove the mock agrees with itself.
/// </para>
/// <para>
/// Fixtures are isolated from the developer's own git configuration. Without that, a machine
/// with <c>core.autocrlf=true</c> — the Windows default — produces different line counts from
/// one without, and a test that passes on one laptop fails on the next for reasons nobody can
/// see in the diff.
/// </para>
/// </summary>
public sealed class FixtureRepository : IDisposable
{
    private FixtureRepository(string root)
    {
        Root = root;

        // Deliberately outside Root and deliberately never created: git treats a missing global
        // config and a missing home as empty ones. Inside Root they would show up as untracked
        // files and quietly corrupt every untracked-file assertion in the suite.
        _isolatedHome = root + "-home";
    }

    private readonly string _isolatedHome;

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
        fixture.WriteFile("readme.md", "fixture\n");
        fixture.Stage("readme.md");
        fixture.Commit("initial");
        return fixture;
    }

    public static FixtureRepository CreateBare()
    {
        var fixture = CreateEmptyDirectory();
        fixture.Git("init", "--bare", "--initial-branch=main");
        fixture.ConfigureIdentity();
        return fixture;
    }

    public string CreateSubdirectory(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string Absolute(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void WriteFile(string relativePath, string contents)
    {
        var path = Absolute(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // No BOM, and the bytes exactly as written: a fixture that silently added a preamble
        // would make every encoding assertion meaningless.
        File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void WriteBytes(string relativePath, byte[] contents)
    {
        var path = Absolute(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
    }

    /// <summary>Writes a file git will call binary: a NUL byte inside the first 8000.</summary>
    public void WriteBinaryFile(string relativePath, int length = 512)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        // Guarantee the NUL regardless of the length chosen.
        bytes[1] = 0;
        WriteBytes(relativePath, bytes);
    }

    /// <summary>Writes a file of roughly <paramref name="bytes"/> bytes of plain text.</summary>
    public void WriteLargeTextFile(string relativePath, long bytes)
    {
        var path = Absolute(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var line = new string('x', 127) + "\n";
        var chunk = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(line, 256)));

        using var stream = File.Create(path);
        for (long written = 0; written < bytes; written += chunk.Length)
        {
            stream.Write(chunk);
        }
    }

    public void Delete(string relativePath) => File.Delete(Absolute(relativePath));

    public void Stage(params string[] relativePaths) => Git(["add", "--", .. relativePaths]);

    /// <summary>Renames through git, so the index records the move.</summary>
    public void Rename(string from, string to) => Git("mv", from, to);

    public void WriteGitignore(params string[] patterns) =>
        WriteFile(".gitignore", string.Join('\n', patterns) + "\n");

    public void Commit(string message) => Git("commit", "--no-gpg-sign", "-m", message);

    public string HeadSha() => GitOutput("rev-parse", "HEAD").Trim();

    /// <summary>
    /// Creates a symbolic link, or returns false when the platform will not allow one.
    /// <para>
    /// Windows needs Developer Mode or elevation for this. A test that silently passed when the
    /// link was never created would be worse than useless, so the caller is told and skips.
    /// </para>
    /// </summary>
    public bool TryCreateSymlink(string relativeLinkPath, string target)
    {
        var path = Absolute(relativeLinkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Points a directory inside the repository at one outside it.
    /// <para>
    /// A symlink where the platform allows one; on Windows, a junction, which is the point of
    /// this method existing separately. Creating a symlink on Windows needs elevation or
    /// Developer Mode, so <see cref="TryCreateSymlink"/> skips on most machines — but a junction
    /// needs neither, is an ordinary reparse point that <c>Directory.ResolveLinkTarget</c>
    /// resolves, and escapes a repository just as effectively. Without it the sandbox's
    /// directory-escape rule would go untested on the one platform this project is verified on.
    /// </para>
    /// </summary>
    public bool TryCreateDirectoryLink(string relativeLinkPath, string targetDirectory)
    {
        var path = Absolute(relativeLinkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Directory.CreateSymbolicLink(path, targetDirectory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }
        }

        // mklink is a cmd builtin, so it has to be run through cmd rather than started directly.
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", path, targetDirectory },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        });

        if (process is null)
        {
            return false;
        }

        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(path);
    }

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

        // Line endings must not depend on whose machine this is. Windows installers default
        // core.autocrlf to true, which would change every line count in these tests.
        Git("config", "core.autocrlf", "false");
        Git("config", "core.safecrlf", "false");
    }

    /// <summary>
    /// Runs git directly rather than through <c>GitProcessRunner</c>: the fixture has to be
    /// able to write, and the runner exists precisely to make that impossible.
    /// </summary>
    public void Git(params string[] arguments) => _ = GitOutput(arguments);

    /// <summary>Runs git and returns its stdout.</summary>
    public string GitOutput(params string[] arguments)
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

        // The developer's own ~/.gitconfig, their global excludes file and the system config are
        // not part of the fixture. Letting any of them in makes tests pass or fail for reasons
        // invisible in this file — a global core.excludesFile can hide a fixture's own files.
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_isolatedHome, ".gitconfig");
        startInfo.Environment["HOME"] = _isolatedHome;
        startInfo.Environment["USERPROFILE"] = _isolatedHome;
        startInfo.Environment["XDG_CONFIG_HOME"] = Path.Combine(_isolatedHome, ".config");

        using var process = Process.Start(startInfo)!;

        // Both pipes have to be drained before waiting. Reading only one deadlocks the moment a
        // command produces more output than the other pipe's buffer holds.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed with {process.ExitCode}: {stderr.GetAwaiter().GetResult()}");
        }

        return stdout.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        DeleteTree(Root);
        DeleteTree(_isolatedHome);
    }

    private static void DeleteTree(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            // git marks object files read-only, which blocks a plain recursive delete.
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
