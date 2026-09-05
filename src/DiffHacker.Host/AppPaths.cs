using System.Runtime.InteropServices;

namespace DiffHacker.Host;

/// <summary>
/// Where DiffHacker keeps per-user state.
/// <para>
/// Resolved by hand rather than through <see cref="Environment.SpecialFolder"/> alone: on
/// macOS, .NET maps <c>LocalApplicationData</c> to <c>~/.local/share</c>, but the platform
/// convention users and support requests expect is <c>~/Library/Application Support</c>.
/// </para>
/// </summary>
public sealed class AppPaths
{
    public const string ApplicationFolderName = "DiffHacker";

    public AppPaths()
        : this(ResolveDataRoot())
    {
    }

    public AppPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    /// <summary>Root of everything DiffHacker writes for this user.</summary>
    public string DataDirectory { get; }

    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>
    /// The rolling log. Serilog appends a date and, on size rollover, an index — so the
    /// current file is <c>log.txt</c> and older ones sit beside it.
    /// </summary>
    public string LogFile => Path.Combine(LogDirectory, "log.txt");

    /// <summary>
    /// Settings, the recent-repository list and provider configuration. Never inside the
    /// user's repository: the application is read-only with respect to what it reviews.
    /// </summary>
    public string DatabaseFile => Path.Combine(DataDirectory, "diffhacker.db");

    /// <summary>API keys, AES-GCM encrypted under the master key.</summary>
    public string SecretsFile => Path.Combine(DataDirectory, "secrets.dat");

    /// <summary>
    /// The master key, wrapped by the platform credential store. Only written by backends that
    /// wrap rather than store — DPAPI on Windows; Keychain and libsecret hold theirs themselves.
    /// </summary>
    public string MasterKeyFile => Path.Combine(DataDirectory, "masterkey.dat");

    /// <summary>Salt for the machine-derived fallback key. Random per install.</summary>
    public string SecretSaltFile => Path.Combine(DataDirectory, "secrets.salt");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    private static string ResolveDataRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", ApplicationFolderName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var root = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                : xdg;

            return Path.Combine(root, ApplicationFolderName);
        }

        // Windows: %LOCALAPPDATA%.
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationFolderName);
    }
}
