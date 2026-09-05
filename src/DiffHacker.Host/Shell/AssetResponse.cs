namespace DiffHacker.Host.Shell;

/// <summary>
/// One asset served to the WebView through the custom scheme handler.
/// </summary>
/// <param name="Content">The bytes. Ownership passes to the shell, which disposes it.</param>
/// <param name="ContentType">A full media type, including charset for text formats.</param>
public sealed record AssetResponse(Stream Content, string ContentType);
