using System.Globalization;
using System.Text;

namespace DiffHacker.Tools;

/// <summary>
/// Builds a tool's answer, and is the only thing that decides when one is too long.
/// <para>
/// Every tool renders through this, so the truncation marker has one spelling and the byte cap
/// has one implementation. A cap enforced separately in nine tools is a cap that is wrong in at
/// least one of them.
/// </para>
/// <para>
/// Plain text rather than JSON, deliberately. The same information as JSON costs roughly twice
/// the tokens once every key is quoted and repeated per row, and §0.2.9 makes token economy an
/// invariant rather than a preference. Models read aligned columns perfectly well.
/// </para>
/// </summary>
public sealed class ToolText
{
    /// <summary>
    /// Room held back from the byte budget so the truncation footer always fits. A result that
    /// filled itself to the last byte and then had nowhere to say so would be the one failure
    /// mode this class exists to prevent.
    /// </summary>
    private const int FooterReserve = 220;

    /// <summary>
    /// The longest single line any tool emits. A minified bundle is one 2 MB line, and without
    /// this a single row would swallow a whole result on its own.
    /// </summary>
    public const int MaxLineLength = 500;

    /// <summary>
    /// Room held back for the header, which is written last.
    /// <para>
    /// Last because a header is only truthful once the body exists. "showing 150 of 600" written
    /// up front becomes a lie the moment the byte cap stops the body at 12 rows, and it would
    /// then sit directly above a footer saying 12 — the kind of contradiction a model resolves
    /// by trusting the wrong one.
    /// </para>
    /// </summary>
    private const int HeaderReserve = 400;

    private readonly StringBuilder _builder = new();
    private readonly int _budget;
    private int _used;

    public ToolText(int maxBytes) => _budget = Math.Max(maxBytes - FooterReserve - HeaderReserve, 256);

    /// <summary>How many lines the body holds.</summary>
    public int LineCount { get; private set; }

    /// <summary>Whether the byte cap stopped the body short of what the caller had to give.</summary>
    public bool HitByteCap { get; private set; }

    /// <summary>
    /// Appends one line, shortening it if it is absurd and refusing it if the result is full.
    /// </summary>
    /// <returns>False when nothing more will fit. Callers stop and report a truncated page.</returns>
    public bool AddLine(string line)
    {
        if (HitByteCap)
        {
            return false;
        }

        var text = Shorten(line ?? string.Empty);
        var cost = Encoding.UTF8.GetByteCount(text) + 1;

        if (_used + cost > _budget)
        {
            HitByteCap = true;
            return false;
        }

        _builder.Append(text).Append('\n');
        _used += cost;
        LineCount++;
        return true;
    }

    /// <summary>
    /// Renders the result: <paramref name="header"/>, the body, then <paramref name="footer"/>
    /// when there is one. Callers build the header from <see cref="LineCount"/> so it describes
    /// what was actually written rather than what was intended.
    /// </summary>
    public string Render(string header, string? footer = null)
    {
        ArgumentNullException.ThrowIfNull(header);

        var text = header + "\n" + _builder.ToString();
        return string.IsNullOrEmpty(footer) ? text : text + footer + "\n";
    }

    /// <summary>
    /// The one truncation marker in the application.
    /// <para>
    /// It states what was shown, out of how many, and how to get the rest — all three, because a
    /// model told only "truncated" will either give up or guess.
    /// </para>
    /// </summary>
    public static string TruncationFooter(
        int shown,
        int total,
        bool totalIsExact,
        string noun,
        string? cursor)
    {
        var counted = totalIsExact
            ? total.ToString(CultureInfo.InvariantCulture)
            : "more than " + total.ToString(CultureInfo.InvariantCulture);

        var marker = string.Create(
            CultureInfo.InvariantCulture,
            $"… truncated: showing {shown} of {counted} {noun}.");

        return cursor is null
            ? marker + " Narrow the query to see the rest."
            : marker + string.Create(CultureInfo.InvariantCulture, $" Call again with cursor=\"{cursor}\" for the next page.");
    }

    /// <summary>Shortens one absurdly long line, saying how much it dropped.</summary>
    private static string Shorten(string line)
    {
        if (line.Length <= MaxLineLength)
        {
            return line;
        }

        var dropped = line.Length - MaxLineLength;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{line[..MaxLineLength]}… [{dropped} more characters on this line]");
    }
}
