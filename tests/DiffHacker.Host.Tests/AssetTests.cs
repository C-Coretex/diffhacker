using System.Text;
using DiffHacker.Host.Assets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Host.Tests;

public sealed class ContentTypesTests
{
    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("assets/index-abc123.js", "text/javascript; charset=utf-8")]
    [InlineData("assets/index-abc123.css", "text/css; charset=utf-8")]
    [InlineData("assets/logo.svg", "image/svg+xml")]
    [InlineData("assets/inter.woff2", "font/woff2")]
    [InlineData("assets/index.js.map", "application/json; charset=utf-8")]
    public void Known_extensions_map_to_their_media_type(string path, string expected) =>
        ContentTypes.ForPath(path).ShouldBe(expected);

    [Fact]
    public void An_unknown_extension_falls_back_rather_than_guessing() =>
        ContentTypes.ForPath("mystery.qqq").ShouldBe(ContentTypes.Fallback);
}

public sealed class DirectoryAssetSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("diffhacker-assets").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Serves_a_file_that_exists()
    {
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html>");

        using var stream = new DirectoryAssetSource(_root).Open("index.html");

        stream.ShouldNotBeNull();
        new StreamReader(stream).ReadToEnd().ShouldBe("<!doctype html>");
    }

    [Fact]
    public void Serves_a_file_in_a_subdirectory_addressed_with_forward_slashes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets"));
        File.WriteAllText(Path.Combine(_root, "assets", "app.js"), "export {};");

        using var stream = new DirectoryAssetSource(_root).Open("assets/app.js");

        stream.ShouldNotBeNull();
    }

    [Fact]
    public void Returns_null_for_a_missing_file() =>
        new DirectoryAssetSource(_root).Open("nope.html").ShouldBeNull();

    [Fact]
    public void Accepts_a_root_given_with_a_trailing_separator()
    {
        // MSBuild hands the dist path over with a trailing separator, and the containment
        // check appends one of its own — which used to reject every asset in Debug builds.
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html>");

        using var stream = new DirectoryAssetSource(_root + Path.DirectorySeparatorChar).Open("index.html");

        stream.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("assets/../../secrets.txt")]
    [InlineData("../../../../../../etc/passwd")]
    public void Refuses_to_escape_the_asset_root(string path)
    {
        // The URL is attacker-influenced in principle: it comes from the WebView.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_root)!, "secrets.txt"), "secret");

        new DirectoryAssetSource(_root).Open(path).ShouldBeNull();
    }
}

public sealed class UiAssetResolverTests
{
    [Fact]
    public void The_start_url_carries_an_explicit_authority()
    {
        // The origin must be diffhacker://app so that CSP 'self' resolves consistently on
        // WebView2, WKWebView and WebKitGTK.
        UiAssetResolver.StartUrl.Scheme.ShouldBe(UiAssetResolver.Scheme);
        UiAssetResolver.StartUrl.Host.ShouldBe(UiAssetResolver.Authority);
        UiAssetResolver.StartUrl.AbsolutePath.ShouldBe("/index.html");
    }

    [Theory]
    [InlineData("diffhacker://app/index.html", "index.html")]
    [InlineData("diffhacker://app/assets/app.js", "assets/app.js")]
    [InlineData("diffhacker://app/", "index.html")]
    public void Maps_a_request_url_to_an_asset_path(string url, string expected)
    {
        var source = new StubAssetSource();
        var resolver = new UiAssetResolver(source, NullLogger<UiAssetResolver>.Instance);

        var response = resolver.Resolve(new Uri(url));

        response.ShouldNotBeNull();
        source.Requested.ShouldBe(expected);
    }

    [Fact]
    public void Returns_null_when_the_asset_is_missing()
    {
        var resolver = new UiAssetResolver(new StubAssetSource(exists: false), NullLogger<UiAssetResolver>.Instance);

        resolver.Resolve(new Uri("diffhacker://app/missing.js")).ShouldBeNull();
    }

    [Fact]
    public void Sets_the_content_type_from_the_asset_path()
    {
        var resolver = new UiAssetResolver(new StubAssetSource(), NullLogger<UiAssetResolver>.Instance);

        resolver.Resolve(new Uri("diffhacker://app/assets/app.css"))!
            .ContentType.ShouldBe("text/css; charset=utf-8");
    }

    private sealed class StubAssetSource(bool exists = true) : IAssetSource
    {
        public string? Requested { get; private set; }

        public string Description => "stub";

        public Stream? Open(string relativePath)
        {
            Requested = relativePath;
            return exists ? new MemoryStream(Encoding.UTF8.GetBytes("content")) : null;
        }
    }
}
