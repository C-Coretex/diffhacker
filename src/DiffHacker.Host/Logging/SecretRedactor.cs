using System.Text.RegularExpressions;

namespace DiffHacker.Host.Logging;

/// <summary>
/// Removes credentials from text on its way to <c>log.txt</c>.
/// <para>
/// CLAUDE.md requires redaction at the sink rather than at each call site, so that forgetting
/// to redact is not something a future call site can do. Two independent passes run: property
/// names that are credentials by definition, and value shapes that are credentials by
/// appearance.
/// </para>
/// </summary>
public static partial class SecretRedactor
{
    public const string Placeholder = "***redacted***";

    /// <summary>
    /// Property names whose value is a credential regardless of what it looks like. Matched as
    /// a case-insensitive substring, so <c>ApiKey</c>, <c>api_key</c> and
    /// <c>OpenAiApiKeyHeader</c> all match.
    /// </summary>
    private static readonly string[] SensitiveNameFragments =
    [
        "apikey",
        "api_key",
        "accesskey",
        "secret",
        "password",
        "passphrase",
        "credential",
        "authorization",
        "bearer",
        "token",
    ];

    public static bool IsSensitiveName(string? name) =>
        name is not null &&
        SensitiveNameFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Scrubs credential-shaped substrings out of already-rendered text. This is what catches
    /// secrets embedded in exception messages and stack traces, which no property-level rule
    /// can reach.
    /// </summary>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        text = BearerToken().Replace(text, "Bearer " + Placeholder);
        text = ProviderKey().Replace(text, Placeholder);
        text = GoogleKey().Replace(text, Placeholder);
        return text;
    }

    /// <summary>HTTP Authorization values.</summary>
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]{8,}=*", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex BearerToken();

    /// <summary>
    /// Prefixed API keys used by the providers Iteration 4 will support: OpenAI (<c>sk-</c>),
    /// Anthropic (<c>sk-ant-</c>), xAI (<c>xai-</c>), Groq (<c>gsk_</c>) and GitHub (<c>ghp_</c>).
    /// </summary>
    [GeneratedRegex(@"\b(?:sk-ant-|sk-|xai-|gsk_|ghp_|github_pat_)[A-Za-z0-9\-_]{12,}", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex ProviderKey();

    /// <summary>Google / Gemini API keys.</summary>
    [GeneratedRegex(@"\bAIza[A-Za-z0-9\-_]{20,}", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex GoogleKey();
}
