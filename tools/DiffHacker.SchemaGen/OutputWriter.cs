using System.Text;

namespace DiffHacker.SchemaGen;

/// <summary>
/// Writes generated files, skipping writes whose content is unchanged so that MSBuild and
/// Vite do not see a modified timestamp on every build, and removing stale output that no
/// longer corresponds to a schema.
/// </summary>
internal sealed class OutputWriter(string directory, string extension)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);

    public string Directory { get; } = directory;

    public int Changed { get; private set; }

    public void Write(string fileName, string content)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var path = Path.Combine(Directory, fileName);
        _written.Add(path);

        // Normalise line endings so the output is identical on every platform.
        var normalised = content.ReplaceLineEndings("\n");

        if (File.Exists(path) &&
            string.Equals(File.ReadAllText(path), normalised, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(path, normalised, Utf8NoBom);
        Changed++;
    }

    /// <summary>
    /// Deletes generated files in the output directory that this run did not produce.
    /// </summary>
    public void RemoveStale()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + extension))
        {
            if (_written.Contains(path))
            {
                continue;
            }

            File.Delete(path);
            Changed++;
            Console.WriteLine($"  removed stale {Path.GetFileName(path)}");
        }
    }
}
