using System.Security.Cryptography;
using System.Text;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Knowledge;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Host.Knowledge;

/// <summary>
/// The only code in DiffHacker that writes into a user's repository.
/// <para>
/// §0.2.12 makes the application read-only with exactly one exception, and this is it. Everything
/// else that touches the filesystem writes to the application's own data directory; a repository
/// is read through git and through <c>File.ReadAllBytes</c> and never through anything that can
/// create, replace or delete. <c>RepositoryWriteTests</c> asserts that by enumerating the source
/// and refusing a write API anywhere but here.
/// </para>
/// <para>
/// The gate is a token, not a boolean. A preview computes a hash over the target and the exact
/// bytes of every file; the export recomputes it from what it is about to write and refuses if it
/// differs. So "nothing is written that was not previewed" survives a bug in the interface, a
/// stale renderer, and anything calling the RPC method directly — the confirmation is over the
/// content, not over the intent.
/// </para>
/// </summary>
public sealed partial class RepositoryDocumentationWriter(ILogger<RepositoryDocumentationWriter> logger)
{
    /// <summary>
    /// Identifies exactly these files, at this target. Any difference in path, order or content
    /// produces a different token and the export is refused.
    /// <para>
    /// Every field is length-prefixed rather than separated by a delimiter. A separator can appear
    /// inside a path or a document, and two different exports that lined up across one would hash
    /// the same — which for this token would mean writing bytes the user approved a different
    /// version of. A length cannot collide with the content it measures.
    /// </para>
    /// </summary>
    public static string TokenFor(DocumentationTarget target, IReadOnlyList<GeneratedDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var builder = new StringBuilder();
        AppendField(builder, target.ToString());

        foreach (var document in documents)
        {
            AppendField(builder, document.RelativePath);
            AppendField(builder, document.Content);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendField(StringBuilder builder, string field) =>
        builder.Append(field.Length).Append(':').Append(field);

    /// <summary>
    /// Reads what is already at a path, or null when nothing is there or it cannot be read as
    /// text. A binary file at the target is reported as unreadable rather than diffed against, so
    /// the preview never shows an empty diff for a file that is not empty.
    /// </summary>
    public static ExistingFile Existing(string repositoryPath, string relativePath)
    {
        var absolute = Path.Combine(repositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolute))
        {
            return new ExistingFile(Exists: false, Text: null);
        }

        try
        {
            var bytes = File.ReadAllBytes(absolute);

            if (TextDecoding.LooksBinary(bytes))
            {
                return new ExistingFile(Exists: true, Text: null);
            }

            return new ExistingFile(Exists: true, Text: TextDecoding.Decode(bytes, out _, out _));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ExistingFile(Exists: true, Text: null);
        }
    }

    /// <summary>
    /// Writes every document, creating directories as needed, and reports which paths already
    /// existed.
    /// </summary>
    /// <exception cref="IOException">A file could not be written.</exception>
    public IReadOnlyList<string> Write(
        string repositoryPath,
        IReadOnlyList<GeneratedDocument> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(documents);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        var overwritten = new List<string>();

        foreach (var document in documents)
        {
            var absolute = Path.GetFullPath(Path.Combine(
                root,
                document.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

            // Containment, even though every path here was produced by DocumentationRenderer from
            // two fixed prefixes and three fixed names. This is the one write path in the product;
            // it does not get to rely on its caller.
            if (!RepositoryPaths.Contains(root, absolute))
            {
                throw new IOException($"'{document.RelativePath}' resolves outside the repository.");
            }

            if (Path.GetDirectoryName(absolute) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(absolute))
            {
                overwritten.Add(document.RelativePath);
            }

            File.WriteAllText(absolute, document.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        WroteDocumentation(logger, documents.Count, overwritten.Count, root);
        return overwritten;
    }

    [LoggerMessage(
        EventId = 6010,
        Level = LogLevel.Warning,
        Message = "Wrote {FileCount} documentation file(s) into {Repository}, replacing {OverwriteCount}. This is the only write path in the application and it ran because the user confirmed a preview.")]
    private static partial void WroteDocumentation(
        ILogger logger,
        int fileCount,
        int overwriteCount,
        string repository);
}

/// <summary>What is already at a path the generator would write to.</summary>
/// <param name="Exists">Whether anything is there.</param>
/// <param name="Text">
/// Its text, or null when there is nothing there or it is not readable as text. Null with
/// <paramref name="Exists"/> true is the case the preview reports as unreadable.
/// </param>
public readonly record struct ExistingFile(bool Exists, string? Text);
