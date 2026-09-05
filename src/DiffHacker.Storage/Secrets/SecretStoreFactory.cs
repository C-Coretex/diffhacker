using DiffHacker.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Storage.Secrets;

/// <summary>
/// Chooses the master-key protector for this machine, and falls back when the platform's
/// credential store is unavailable.
/// <para>
/// The fallback is not an edge case: headless and minimal Linux installs routinely have no
/// keyring daemon, which is exactly why Iteration 2 requires one. What matters is that the
/// application keeps working and <i>says</i> which backend it ended up on, rather than
/// claiming a keyring it does not have.
/// </para>
/// </summary>
public static class SecretStoreFactory
{
    /// <summary>
    /// Opens the secret store, probing the platform backend and degrading to the machine-derived
    /// key on any failure.
    /// </summary>
    /// <param name="secretsFile">Where the encrypted secrets live.</param>
    /// <param name="masterKeyFile">Where a wrapped master key lives, on backends that wrap one.</param>
    /// <param name="saltFile">Where the fallback's salt lives.</param>
    /// <param name="logger">Records which backend won, and why the others did not.</param>
    public static ISecretStore Create(string secretsFile, string masterKeyFile, string saltFile, ILogger logger) =>
        Create(secretsFile, masterKeyFile, saltFile, logger, platformProtector: null);

    /// <param name="platformProtector">
    /// Overrides which platform backend is tried. Exists so tests can stand in an unavailable
    /// keyring — the case this whole fallback is for, and the one that cannot be reproduced on
    /// a machine whose credential store works.
    /// </param>
    /// <inheritdoc cref="Create(string, string, string, ILogger)"/>
    internal static FileSecretStore Create(
        string secretsFile,
        string masterKeyFile,
        string saltFile,
        ILogger logger,
        Func<string, IMasterKeyProtector?>? platformProtector)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var platform = (platformProtector ?? CreatePlatformProtector)(masterKeyFile);

        if (platform is not null && TryProbe(platform, logger))
        {
            return new FileSecretStore(secretsFile, platform, isFallback: false);
        }

        var fallback = new MachineDerivedMasterKeyProtector(saltFile);
        SecretBackendChosen(logger, fallback.Backend.ToString(), platform is null);

        return new FileSecretStore(secretsFile, fallback, isFallback: true);
    }

    private static IMasterKeyProtector? CreatePlatformProtector(string masterKeyFile)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiMasterKeyProtector(masterKeyFile);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new KeychainMasterKeyProtector();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LibsecretMasterKeyProtector();
        }

        return null;
    }

    /// <summary>
    /// Actually asks the backend for the key. Nothing short of that distinguishes "libsecret is
    /// present" from "libsecret is present and a keyring daemon is listening".
    /// </summary>
#pragma warning disable CA1031 // Any failure here is a reason to fall back, whatever its type.
    private static bool TryProbe(IMasterKeyProtector protector, ILogger logger)
    {
        try
        {
            var key = protector.GetOrCreateMasterKey();
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            SecretBackendChosen(logger, protector.Backend.ToString(), false);
            return true;
        }
        catch (Exception ex)
        {
            SecretBackendUnavailable(logger, protector.Backend.ToString(), ex.Message);
            return false;
        }
    }
#pragma warning restore CA1031

    private static readonly Action<ILogger, string, bool, Exception?> SecretBackendChosenMessage =
        LoggerMessage.Define<string, bool>(
            LogLevel.Information,
            new EventId(3010, nameof(SecretBackendChosen)),
            "Secret store backend: {Backend} (no platform backend compiled in: {Unsupported}).");

    private static readonly Action<ILogger, string, string, Exception?> SecretBackendUnavailableMessage =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3011, nameof(SecretBackendUnavailable)),
            "Secret store backend {Backend} is unavailable, falling back to a machine-derived key: {Reason}");

    private static void SecretBackendChosen(ILogger logger, string backend, bool unsupported) =>
        SecretBackendChosenMessage(logger, backend, unsupported, null);

    private static void SecretBackendUnavailable(ILogger logger, string backend, string reason) =>
        SecretBackendUnavailableMessage(logger, backend, reason, null);
}
