using System.Text;

namespace DiffHacker.Git;

/// <summary>
/// Reads NUL-terminated fields out of a stream without buffering the stream.
/// <para>
/// Every git command in this layer is asked for <c>-z</c> output, because that is the only way
/// to read a path safely: the human-facing formats quote, escape and truncate paths depending on
/// <c>core.quotePath</c> and the terminal, and CLAUDE.md is explicit that this application never
/// parses those. <c>-z</c> emits path bytes verbatim between NULs, so this reader is the whole
/// of the "parsing" involved.
/// </para>
/// </summary>
internal sealed class NulFieldReader(Stream stream, int bufferSize = 64 * 1024)
{
    private readonly byte[] _buffer = new byte[bufferSize];
    private int _length;
    private int _position;

    /// <summary>
    /// Assembles one field across buffer boundaries. Reused between fields, so a changeset of a
    /// thousand paths does not allocate a thousand accumulators.
    /// </summary>
    private byte[] _field = new byte[512];
    private int _fieldLength;

    /// <summary>
    /// The next field, or null at end of stream. A trailing field with no terminating NUL is
    /// still returned — git does terminate every record, but truncated output should surface as
    /// a parse failure rather than a silently dropped file.
    /// </summary>
    public async ValueTask<string?> ReadFieldAsync(CancellationToken cancellationToken)
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
            var terminator = span.IndexOf((byte)0);

            if (terminator < 0)
            {
                Accumulate(span);
                _position = _length;
                sawAnything = true;
                continue;
            }

            Accumulate(span[..terminator]);
            _position += terminator + 1;
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
    /// Paths reach us as raw bytes. Almost every repository's paths are UTF-8 or ASCII; for the
    /// rest, replacement characters keep the file visible in the list rather than aborting the
    /// whole changeset over one undecodable name.
    /// </summary>
    private string Decode() => Encoding.UTF8.GetString(_field, 0, _fieldLength);
}
