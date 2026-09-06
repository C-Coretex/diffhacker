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
    public void State_files_sit_in_the_data_directory_not_in_the_users_repository()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "diffhacker-test"));

        // §0.2.12 makes the app read-only towards the repositories it reviews, so everything it
        // writes belongs here and nowhere else.
        paths.DatabaseFile.ShouldBe(Path.Combine(paths.DataDirectory, "diffhacker.db"));
        paths.SecretsFile.ShouldBe(Path.Combine(paths.DataDirectory, "secrets.dat"));
        paths.MasterKeyFile.ShouldBe(Path.Combine(paths.DataDirectory, "masterkey.dat"));
        paths.SecretSaltFile.ShouldBe(Path.Combine(paths.DataDirectory, "secrets.salt"));
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

        options.Verbose.ShouldBeFalse();
        options.DataDirectory.ShouldBeNull();
    }

    [Fact]
    public void Parses_verbose_logging()
    {
        HostCommandLine.Parse(["--verbose"]).Verbose.ShouldBeTrue();
    }

    [Fact]
    public void Ignores_arguments_it_does_not_recognise()
    {
        // Some OS shells append their own arguments when launching a bundled application.
        var options = HostCommandLine.Parse(["-psn_0_12345", "--verbose"]);

        options.Verbose.ShouldBeTrue();
    }

    [Fact]
    public void A_data_directory_switch_with_no_value_is_ignored_rather_than_fatal()
    {
        // Trailing switch with nothing after it. Crashing the application on start-up over a
        // malformed argument would be a worse outcome than falling back to the real directory.
        HostCommandLine.Parse(["--data-dir"]).DataDirectory.ShouldBeNull();
    }

    [Fact]
    public void Parses_an_explicit_data_directory()
    {
        var options = HostCommandLine.Parse(["--data-dir", "/tmp/somewhere"]);

        options.DataDirectory.ShouldBe("/tmp/somewhere");
    }

    [Fact]
    public void An_explicit_data_directory_is_where_state_goes()
    {
        var root = Path.Combine(Path.GetTempPath(), "diffhacker-datadir-" + Guid.NewGuid().ToString("n"));

        var paths = Program.ResolvePaths(HostCommandLine.Parse(["--data-dir", root]));

        // The end-to-end suite writes throwaway providers and API keys. Without this switch it
        // would write them into the developer's real secret store, because .NET resolves the
        // per-user directory through the Win32 known-folder API and no environment variable can
        // redirect it.
        paths.DataDirectory.ShouldBe(Path.GetFullPath(root));
    }

    [Fact]
    public void Without_the_switch_state_goes_to_the_per_user_directory()
    {
        var paths = Program.ResolvePaths(HostCommandLine.Parse([]));

        paths.DataDirectory.ShouldBe(new AppPaths().DataDirectory);
    }
}
