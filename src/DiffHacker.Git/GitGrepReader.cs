using System.Globalization;
using System.Text;
using DiffHacker.Core.Changes;

namespace DiffHacker.Git;

/// <summary>
/// Reads the records <c>git grep -z -n</c> writes.
/// <para>
/// The format is <c>&lt;path&gt;NUL&lt;line number&gt;NUL&lt;line&gt;LF</c>. Note what <c>-z</c>
/// does: it replaces <i>both</i> separators with NUL, not just the one after the path. That is
/// the whole reason <see cref="NulFieldReader"/> is not reused here — the third field is
/// terminated by a newline, not by a NUL, and a reader that assumed otherwise would swallow the
/// following record's path.
/// </para>
/// <para>
/// It is also why this layer never asks git for context lines. Without <c>-z</c> git marks a
/// match with <c>:</c> and a context line with <c>-</c>; with <c>-z</c> both become NUL and the
/// distinction is gone, so <c>-C</c> would return lines with no way to tell which of them
/// actually matched. Dropping <c>-z</c> to recover that would mean parsing quoted, escaped,
/// <c>core.quotePath</c>-dependent paths, which CLAUDE.md forbids outright. Match positions come
/// from git; the lines around them are read from the file afterwards.
/// </para>
/// </summary>
internal sealed class GitGrepReader(Stream stream, int bufferSize = 64 * 1024)
{
    private readonly byte[] _buffer = new byte[bufferSize];
    private int _length;
    private int _position;

    private byte[] _field = new byte[512];
    private int _fieldLength;

    /// <summary>
    /// The next match, or null at end of stream.
    /// </summary>
    /// <returns>
    /// Null once the stream ends. A record that does not parse — a line number that is not a
    /// number, a truncated final record — is skipped rather than thrown on: one unreadable line
    /// should cost that line, not the whole search.
    /// </returns>
    public async ValueTask<GrepMatch?> ReadMatchAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var path = await ReadFieldAsync(terminator: 0, cancellationToken).ConfigureAwait(false);
            if (path is null)
            {
                return null;
            }

            var number = await ReadFieldAsync(terminator: 0, cancellationToken).ConfigureAwait(false);
            if (number is null)
            {
                return null;
            }

            var line = await ReadFieldAsync(terminator: (byte)'\n', cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (path.Length == 0 ||
                !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var lineNumber))
            {
                continue;
            }

            // git grep still emits the "--" hunk separator when it has one to emit. It arrives as
            // a record whose path field is "--", which the parse above cannot mistake for a real
            // one because the line-number field then fails to parse.
            return new GrepMatch(path, lineNumber, line.TrimEnd('\r'));
        }
    }

    private async ValueTask<string?> ReadFieldAsync(byte terminator, CancellationToken cancellationToken)
    {
        _fieldLength = 0;
        var sawAnything = false;

        while (true)
        {
            if (_position >= _length)
            {
                _length = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                _position = 0;

                if (_length == 0)
                {
                    return sawAnything ? Decode() : null;
                }
            }

            var span = _buffer.AsSpan(_position, _length - _position);
            var index = span.IndexOf(terminator);

            if (index < 0)
            {
                Accumulate(span);
                _position = _length;
                sawAnything = true;
                continue;
            }

            Accumulate(span[..index]);
            _position += index + 1;
            return Decode();
        }
    }

    private void Accumulate(ReadOnlySpan<byte> bytes)
    {
        if (_fieldLength + bytes.Length > _field.Length)
        {
            Array.Resize(ref _field, Math.Max(_field.Length * 2, _fieldLength + bytes.Length));
        }

        bytes.CopyTo(_field.AsSpan(_fieldLength));
        _fieldLength += bytes.Length;
    }

    /// <summary>
    /// Matched lines reach us as raw bytes from whatever encoding the file is in. UTF-8 with
    /// replacement characters keeps a Latin-1 line legible enough to locate, and the caller can
    /// read the file properly through <see cref="TextDecoding"/> when it wants the real text.
    /// </summary>
    private string Decode() => Encoding.UTF8.GetString(_field, 0, _fieldLength);
}
