using System.Text;
using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Tests;

/// <summary>
/// <see cref="TextDecoding.Encode"/> is <see cref="TextDecoding.Decode"/> in reverse, and the
/// property worth pinning is the round trip: text decoded from a file in some encoding, saved
/// back through an edit, has to land on disk in that same encoding — not silently rewritten as
/// UTF-8, which is what an editor that only ever called <c>Encoding.UTF8.GetBytes</c> would do to
/// a Latin-1 or UTF-16 file the first time someone edited it.
/// </summary>
public sealed class TextDecodingTests
{
    // Plain ASCII text on purpose: every one of these encodings other than Latin-1 is told apart
    // from the others by its byte order mark, not by its content, so ASCII bytes round-trip
    // through all of them without ever exercising the fallback. Latin-1 has no mark at all — it is
    // told apart from UTF-8 only by content that UTF-8 cannot decode — so it gets its own test
    // below with a character ASCII does not have.
    [Theory]
    [InlineData(TextDecoding.Utf8)]
    [InlineData(TextDecoding.Utf8Bom)]
    [InlineData(TextDecoding.Utf16Le)]
    [InlineData(TextDecoding.Utf16Be)]
    [InlineData(TextDecoding.Utf32Le)]
    [InlineData(TextDecoding.Utf32Be)]
    public void Encoding_then_decoding_returns_the_original_text_and_names_the_same_encoding(string encoding)
    {
        const string text = "line one\nline two\n";

        var bytes = TextDecoding.Encode(text, encoding);
        var decoded = TextDecoding.Decode(bytes, out var reportedEncoding, out var usedFallback);

        decoded.ShouldBe(text);
        reportedEncoding.ShouldBe(encoding);
        usedFallback.ShouldBeFalse();
    }

    [Fact]
    public void Latin1_round_trips_a_character_utf8_could_not_have_decoded()
    {
        const string text = "café\n";

        var bytes = TextDecoding.Encode(text, TextDecoding.Latin1);
        var decoded = TextDecoding.Decode(bytes, out var reportedEncoding, out var usedFallback);

        decoded.ShouldBe(text);
        reportedEncoding.ShouldBe(TextDecoding.Latin1);
        usedFallback.ShouldBeTrue();
    }

    [Fact]
    public void An_unrecognised_encoding_name_falls_back_to_plain_utf8()
    {
        var bytes = TextDecoding.Encode("hello", "made-up-encoding");

        bytes.ShouldBe(Encoding.UTF8.GetBytes("hello"));
    }

    [Fact]
    public void A_missing_encoding_name_falls_back_to_plain_utf8()
    {
        var bytes = TextDecoding.Encode("hello", null);

        bytes.ShouldBe(Encoding.UTF8.GetBytes("hello"));
    }

    [Fact]
    public void Latin1_writes_every_byte_the_original_decode_could_have_produced()
    {
        // Every byte 0-255 is a valid Latin-1 character, which is what makes it a safe fallback on
        // the read side. The write side has to be equally total, or a round trip could throw for a
        // file nobody edited outside that range.
        var allBytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var text = TextDecoding.Decode(allBytes, out _, out _);

        TextDecoding.Encode(text, TextDecoding.Latin1).ShouldBe(allBytes);
    }
}
