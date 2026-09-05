using System.Runtime.InteropServices;

namespace DiffHacker.Host.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Log_file_sits_under_a_logs_folder_in_the_data_directory()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "diffhacker-test"));

        paths.LogDirectory.ShouldBe(Path.Combine(paths.DataDirectory, "logs"));
        paths.LogFile.ShouldBe(Path.Combine(paths.LogDirectory, "log.txt"));
    }

    [Fact]
    public void The_default_data_directory_follows_the_platform_convention()
    {
        var paths = new AppPaths();

        paths.DataDirectory.ShouldEndWith(AppPaths.ApplicationFolderName);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // .NET maps LocalApplicationData to ~/.local/share on macOS, which is not where a
            // macOS user or a support request would look.
            paths.DataDirectory.ShouldContain(Path.Combine("Library", "Application Support"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            paths.DataDirectory.ShouldContain(".local");
        }
        else
        {
            paths.DataDirectory.ShouldStartWith(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }
    }

    [Fact]
    public void EnsureCreated_creates_the_log_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "diffhacker-" + Guid.NewGuid().ToString("n"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreated();

            Directory.Exists(paths.LogDirectory).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

public sealed class HostCommandLineTests
{
    [Fact]
    public void Defaults_to_an_interactive_run()
    {
        var options = HostCommandLine.Parse([]);

        options.SelfTest.ShouldBeFalse();
        options.Verbose.ShouldBeFalse();
    }

    [Fact]
    public void Parses_the_self_test_switches_CI_uses()
    {
        var options = HostCommandLine.Parse(["--self-test", "--timeout", "45", "--out", "result.json", "--verbose"]);

        options.SelfTest.ShouldBeTrue();
        options.SelfTestTimeout.ShouldBe(TimeSpan.FromSeconds(45));
        options.SelfTestOutputPath.ShouldBe("result.json");
        options.Verbose.ShouldBeTrue();
    }

    [Fact]
    public void Ignores_arguments_it_does_not_recognise()
    {
        // Some OS shells append their own arguments when launching a bundled application.
        var options = HostCommandLine.Parse(["-psn_0_12345", "--self-test"]);

        options.SelfTest.ShouldBeTrue();
    }
}
