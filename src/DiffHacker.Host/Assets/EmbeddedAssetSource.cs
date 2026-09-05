using System.Reflection;

namespace DiffHacker.Host.Assets;

/// <summary>
/// Serves the renderer from resources embedded in this assembly. Used for Release builds, so
/// a shipped DiffHacker is a self-contained binary with no loose web assets beside it.
/// </summary>
public sealed class EmbeddedAssetSource : IAssetSource
{
    /// <summary>Resource name prefix applied by the UI embedding target in the csproj.</summary>
    public const string Prefix = "ui/";

    private readonly Assembly _assembly;
    private readonly HashSet<string> _names;

    public EmbeddedAssetSource(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _assembly = assembly;
        _names = assembly.GetManifestResourceNames()
            .Where(static name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    public string Description => $"embedded resources ({_names.Count} asset(s))";

    public Stream? Open(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var name = Prefix + relativePath;
        return _names.Contains(name) ? _assembly.GetManifestResourceStream(name) : null;
    }
}
