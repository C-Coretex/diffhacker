namespace DiffHacker.Host.Assets;

/// <summary>
/// Supplies the Vite-built renderer bundle. Embedded in the assembly for Release builds,
/// read from <c>src/ui/dist</c> for Debug builds so <c>vite build --watch</c> is a usable
/// inner loop without introducing an HTTP server.
/// </summary>
public interface IAssetSource
{
    /// <summary>A description of where assets are coming from, for the log.</summary>
    string Description { get; }

    /// <summary>
    /// Opens an asset by its forward-slash relative path, or returns <see langword="null"/>
    /// when there is no such asset.
    /// </summary>
    Stream? Open(string relativePath);
}
