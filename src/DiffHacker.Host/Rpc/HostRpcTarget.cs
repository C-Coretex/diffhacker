using System.Reflection;
using System.Runtime.InteropServices;
using DiffHacker.Contracts;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Host identity. <c>host.ping</c> is the handshake every renderer performs on start-up; it is
/// how a host and a renderer built from different contract generations are caught immediately
/// rather than by a confusing failure later.
/// </summary>
public sealed class HostRpcTarget(HostRuntimeInfo runtime, ILogger<HostRpcTarget> logger)
{
    [JsonRpcMethod("host.ping")]
    public HostInfo Ping()
    {
        logger.LogInformation("host.ping (contract {ContractVersion})", ContractVersion.Current);

        return new HostInfo(
            appVersion: runtime.AppVersion,
            contractVersion: ContractVersion.Current,
            osDescription: RuntimeInformation.OSDescription,
            platform: runtime.Platform,
            processArchitecture: RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            startedAtUtc: runtime.StartedAtUtc);
    }
}

/// <summary>
/// Immutable facts about this process, resolved once at start-up.
/// </summary>
public sealed record HostRuntimeInfo
{
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string AppVersion { get; init; } =
        typeof(HostRuntimeInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HostRuntimeInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public HostInfoPlatform Platform { get; init; } = ResolvePlatform();

    private static HostInfoPlatform ResolvePlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return HostInfoPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return HostInfoPlatform.Macos;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return HostInfoPlatform.Linux;
        }

        throw new PlatformNotSupportedException(RuntimeInformation.OSDescription);
    }
}
