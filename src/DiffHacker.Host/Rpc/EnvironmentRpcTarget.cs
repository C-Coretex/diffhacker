using DiffHacker.Contracts;
using DiffHacker.Core.Repositories;
using DiffHacker.Core.Secrets;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Reports the two external facilities the application depends on: the git command line and
/// wherever secrets are being kept.
/// <para>
/// The renderer calls this on start-up. Without git the application is non-functional and says
/// so plainly rather than failing at the first repository (requirement 6); the secret backend
/// is reported so the interface can state the real guarantee instead of an assumed one.
/// </para>
/// </summary>
public sealed class EnvironmentRpcTarget(IGitEnvironment git, ISecretStore secrets)
{
    [JsonRpcMethod("environment.describe")]
    public async Task<EnvironmentInfo> DescribeAsync(CancellationToken cancellationToken)
    {
        var availability = await git.ProbeAsync(cancellationToken).ConfigureAwait(false);

        return new EnvironmentInfo(
            gitAvailable: availability.Available,
            gitVersion: availability.Version,
            secretBackend: ToWire(secrets.Backend),
            secretBackendIsFallback: secrets.IsFallback);
    }

    private static EnvironmentInfoSecretBackend ToWire(SecretBackendKind backend) => backend switch
    {
        SecretBackendKind.WindowsDpapi => EnvironmentInfoSecretBackend.Windows_dpapi,
        SecretBackendKind.MacosKeychain => EnvironmentInfoSecretBackend.Macos_keychain,
        SecretBackendKind.LinuxLibsecret => EnvironmentInfoSecretBackend.Linux_libsecret,
        SecretBackendKind.MachineDerived => EnvironmentInfoSecretBackend.Machine_derived,
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown secret backend."),
    };
}
