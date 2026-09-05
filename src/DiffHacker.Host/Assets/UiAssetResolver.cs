using DiffHacker.Host.Shell;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Host.Assets;

/// <summary>
/// Turns a <c>diffhacker://app/...</c> request into an asset response.
/// <para>
/// The authority is explicit so that the document origin is <c>diffhacker://app</c> on all
/// three WebViews. An authority-less form such as <c>app://./index.html</c> parses
/// inconsistently across WebView2, WKWebView and WebKitGTK.
/// </para>
/// </summary>
public sealed class UiAssetResolver(IAssetSource source, ILogger<UiAssetResolver> logger)
{
    /// <summary>The custom scheme. Lower case: Photino normalises and compares in lower case.</summary>
    public const string Scheme = "diffhacker";

    /// <summary>The authority, which together with the scheme forms the document origin.</summary>
    public const string Authority = "app";

    /// <summary>Served when the request has no path.</summary>
    public const string DefaultDocument = "index.html";

    public static Uri StartUrl { get; } = new($"{Scheme}://{Authority}/{DefaultDocument}");

    public AssetResponse? Resolve(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var relativePath = uri.AbsolutePath.TrimStart('/');
        if (relativePath.Length == 0)
        {
            relativePath = DefaultDocument;
        }

        var content = source.Open(relativePath);
        if (content is null)
        {
            logger.LogWarning("Asset not found: {Path} (source: {Source})", relativePath, source.Description);
            return null;
        }

        logger.LogDebug("Served {Path}", relativePath);
        return new AssetResponse(content, ContentTypes.ForPath(relativePath));
    }
}
