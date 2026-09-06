using System.Text;

namespace DiffHacker.Core.Changes;

/// <summary>
/// How this application turns bytes on disk into text, decided once so nothing downstream has
/// to guess.
/// <para>
/// The rules, in order: content with a NUL byte in its first
/// <see cref="ContentLimits.BinarySniffBytes"/> is binary and is never decoded at all; a byte
/// order mark is believed; otherwise strict UTF-8 is tried; otherwise Latin-1, which cannot
/// fail. The result always says which encoding was used and whether the fallback was taken, so
/// Iteration 5's toolbox and Iteration 10's viewer can both report it honestly rather than
/// showing replacement characters as if they were the file's content.
/// </para>
/// <para>
/// Latin-1 rather than Windows-1252 because it is built into the framework: the repository sets
/// <c>InvariantGlobalization</c>, so the Windows code pages are unavailable without adding a
/// package for what is already a best-effort guess.
/// </para>
/// </summary>
public static class TextDecoding
{
    public const string Utf8 = "utf-8";
    public const string Utf8Bom = "utf-8-bom";
    public const string Utf16Le = "utf-16le";
    public const string Utf16Be = "utf-16be";
    public const string Utf32Le = "utf-32le";
    public const string Utf32Be = "utf-32be";
    public const string Latin1 = "iso-8859-1";

    private static readonly byte[] Utf8BomBytes = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf32LeBomBytes = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BeBomBytes = [0x00, 0x00, 0xFE, 0xFF];
    private static readonly byte[] Utf16LeBomBytes = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBomBytes = [0xFE, 0xFF];

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly UTF32Encoding Utf32BigEndian =
        new(bigEndian: true, byteOrderMark: false);

    /// <summary>True when git would call these bytes binary: a NUL in the sniffed prefix.</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> prefix)
    {
        var sniff = prefix.Length > ContentLimits.BinarySniffBytes
            ? prefix[..ContentLimits.BinarySniffBytes]
            : prefix;

        return sniff.IndexOf((byte)0) >= 0;
    }

    /// <summary>Decodes bytes already known not to be binary.</summary>
    /// <param name="bytes">The whole content.</param>
    /// <param name="encoding">Name of the encoding that was used.</param>
    /// <param name="usedFallback">True when the bytes were not valid UTF-8.</param>
    public static string Decode(ReadOnlySpan<byte> bytes, out string encoding, out bool usedFallback)
    {
        usedFallback = false;

        if (bytes.StartsWith(Utf8BomBytes))
        {
            encoding = Utf8Bom;
            return Encoding.UTF8.GetString(bytes[Utf8BomBytes.Length..]);
        }

        // A UTF-32LE mark begins with the UTF-16LE mark, so the wider one has to be tested first.
        if (bytes.StartsWith(Utf32LeBomBytes))
        {
            encoding = Utf32Le;
            return Encoding.UTF32.GetString(bytes[Utf32LeBomBytes.Length..]);
        }

        if (bytes.StartsWith(Utf32BeBomBytes))
        {
            encoding = Utf32Be;
            return Utf32BigEndian.GetString(bytes[Utf32BeBomBytes.Length..]);
        }

        if (bytes.StartsWith(Utf16LeBomBytes))
        {
            encoding = Utf16Le;
            return Encoding.Unicode.GetString(bytes[Utf16LeBomBytes.Length..]);
        }

        if (bytes.StartsWith(Utf16BeBomBytes))
        {
            encoding = Utf16Be;
            return Encoding.BigEndianUnicode.GetString(bytes[Utf16BeBomBytes.Length..]);
        }

        try
        {
            encoding = Utf8;
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8. Latin-1 maps every possible byte to a character, so this cannot throw
            // and cannot lose bytes — it may simply be the wrong alphabet, which is exactly what
            // usedFallback exists to admit.
            encoding = Latin1;
            usedFallback = true;
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
