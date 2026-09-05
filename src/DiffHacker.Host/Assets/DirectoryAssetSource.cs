namespace DiffHacker.Host.Assets;

/// <summary>
/// Serves the renderer from <c>src/ui/dist</c> on disk. Debug builds only: it makes
/// <c>vite build --watch</c> plus a window reload the inner loop, without the localhost dev
/// server that CLAUDE.md rules out.
/// </summary>
public sealed class DirectoryAssetSource : IAssetSource
{
    private readonly string _root;

    public DirectoryAssetSource(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        // Path.GetFullPath keeps a trailing separator, and the containment check below appends
        // one. Without trimming first, a root handed over as "…/dist/" would compare against
        // "…/dist//" and reject every asset.
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public string Description => $"directory {_root}";

    public Stream? Open(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var candidate = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        // The URL comes from the WebView; never let it escape the asset root.
        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return File.Exists(candidate)
            ? new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
    }
}
