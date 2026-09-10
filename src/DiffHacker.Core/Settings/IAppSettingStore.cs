namespace DiffHacker.Core.Settings;

/// <summary>
/// Single named values that belong to the application rather than to a repository, a provider or an
/// analysis — the settings too small to deserve a table.
/// <para>
/// Deliberately untyped. Everything reachable through here is a short string the user chose, and a
/// caller that needs it to mean something parses it itself: a store that knew the meaning of every
/// key would grow a method per setting, which is the shape this exists to avoid.
/// </para>
/// <para>
/// Nothing secret goes in here. Keys and values are stored in plain text, and every credential in
/// this application goes to <see cref="DiffHacker.Core.Secrets.ISecretStore"/> instead.
/// </para>
/// </summary>
public interface IAppSettingStore
{
    /// <summary>The stored value, or null when the key was never set.</summary>
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a value, replacing any it had. A null or empty value forgets the key rather than
    /// storing emptiness, so "never set" and "set to nothing" cannot drift apart.
    /// </summary>
    ValueTask SetAsync(string key, string? value, CancellationToken cancellationToken);
}
