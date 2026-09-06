using System.Diagnostics;
using System.Text;

namespace DiffHacker.Host.Tests;

/// <summary>
/// A throwaway git repository, built the hard way.
/// <para>
/// Deliberately a separate, smaller helper than the one in <c>DiffHacker.Git.Tests</c>: the host
/// tests need one repository with a handful of files, not the fixture vocabulary that suite
/// needs, and a test project referencing another test project to borrow a fixture is a
/// dependency nobody wants to explain later.
/// </para>
/// </summary>
internal sealed class TemporaryRepository : IDisposable
{
    private readonly string _isolatedHome;

    private TemporaryRepository(string root)
    {
        Root = root;
        _isolatedHome = root + "-home";
    }

    public string Root { get; }

    public static TemporaryRepository CreateWithCommit()
    {
        var repository = new TemporaryRepository(Directory.CreateTempSubdirectory("diffhacker-host-").FullName);

        repository.Git("init", "--initial-branch=main");
        repository.Git("config", "user.email", "fixture@diffhacker.test");
        repository.Git("config", "user.name", "DiffHacker Fixture");
        repository.Git("config", "commit.gpgsign", "false");
        repository.Git("config", "core.autocrlf", "false");

        return repository;
    }

    public void Write(string relativePath, string contents)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void Commit(string message)
    {
        Git("add", "--all");
        Git("commit", "--no-gpg-sign", "-m", message);
    }

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
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_isolatedHome, ".gitconfig");
        startInfo.Environment["HOME"] = _isolatedHome;
        startInfo.Environment["USERPROFILE"] = _isolatedHome;
        startInfo.Environment["XDG_CONFIG_HOME"] = Path.Combine(_isolatedHome, ".config");

        using var process = Process.Start(startInfo)!;

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {stderr.GetAwaiter().GetResult()}");
        }

        _ = stdout.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Delete(Root);
        Delete(_isolatedHome);
    }

    private static void Delete(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

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
