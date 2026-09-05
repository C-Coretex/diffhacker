namespace DiffHacker.Core.Secrets;

/// <summary>
/// Which facility is protecting the master key that encrypts stored API keys.
/// <para>
/// Reported to the interface so it can state the real guarantee. <see cref="MachineDerived"/>
/// is a genuinely weaker promise than the other three and the interface says so, rather than
/// claiming an OS keyring that is not there.
/// </para>
/// </summary>
public enum SecretBackendKind
{
    /// <summary>Windows DPAPI, keyed to the signed-in user account.</summary>
    WindowsDpapi,

    /// <summary>macOS Keychain Services.</summary>
    MacosKeychain,

    /// <summary>The Secret Service API via libsecret, on Linux.</summary>
    LinuxLibsecret,

    /// <summary>
    /// Fallback for systems with no keyring daemon — common on headless and minimal Linux
    /// installs. The key is derived from a per-install random salt plus machine and user
    /// identifiers, which protects a copied file but not an attacker who already has the
    /// user's account.
    /// </summary>
    MachineDerived,
}
