using System.Globalization;
using DiffHacker.Core.Changes;

namespace DiffHacker.Tools;

/// <summary>
/// How the toolbox spells things, so nine tools spell them the same way.
/// </summary>
internal static class ToolFormat
{
    /// <inheritdoc cref="ChangedFileText.Status"/>
    public static string Status(ChangeStatus status) => ChangedFileText.Status(status);

    /// <summary>The snapshot stamp every result header carries, so a stale answer is a legible one.</summary>
    public static string Timestamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Sizes a person would read, because a model reads them the same way.</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        if (bytes < 1024 * 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB");
    }

    /// <inheritdoc cref="ChangedFileText.Row"/>
    public static string ChangedRow(ChangedFile file, bool withheld = false) =>
        ChangedFileText.Row(file, withheld);

    /// <inheritdoc cref="ChangedFileText.Legend"/>
    public const string ChangedRowLegend = ChangedFileText.Legend;
}
