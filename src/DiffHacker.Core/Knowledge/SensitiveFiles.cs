namespace DiffHacker.Core.Knowledge;

/// <summary>
/// Files whose contents never reach a model.
/// <para>
/// The toolbox already sees only what git sees, which keeps <c>.git/</c> and everything
/// <c>.gitignore</c> covers out of reach. That is not enough: a committed <c>.env</c>, a
/// checked-in private key or an <c>.npmrc</c> with a registry token are all things git happily
/// tracks and a model would happily read.
/// </para>
/// <para>
/// Matching files are <b>listed and flagged, never hidden</b>. Hiding them would be simpler and
/// would be wrong: a changed <c>.env</c> is a real part of a changeset, §0.2.5 says every changed
/// file appears in the graph, and a reviewer who cannot see that the file changed is worse off
/// than one who can see it changed but not how. So the path stays visible everywhere and only the
/// content is refused.
/// </para>
/// <para>
/// This type holds the policy, not the matching. The glob engine lives with the toolbox, which is
/// the only thing that resolves a path; the host needs the list itself to show the user what is in
/// force.
/// </para>
/// </summary>
public static class SensitiveFiles
{
    /// <summary>
    /// The built-in list. Deliberately about secret-bearing <i>names</i> rather than about
    /// content: a rule that has to open a file to decide whether it may be opened is no rule at
    /// all. Certificates are absent because a public certificate is public; private key material
    /// and credential files are what is here.
    /// <para>
    /// Every entry is written with a leading <c>**/</c>, which the glob engine treats as matching
    /// zero directories as well as many — so <c>**/.env</c> covers the one at the repository root
    /// and the one three directories down, and no entry needs a rootless twin.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> DefaultGlobs { get; } =
    [
        "**/.env",
        "**/.env.*",
        "**/*.pem",
        "**/*.key",
        "**/*.pfx",
        "**/*.p12",
        "**/*.jks",
        "**/*.keystore",
        "**/id_rsa*",
        "**/id_ecdsa*",
        "**/id_ed25519*",
        "**/.npmrc",
        "**/.pypirc",
        "**/.netrc",
        "**/_netrc",
        "**/secrets.*",
        "**/credentials",
        "**/*.tfvars",
    ];

    /// <summary>
    /// The built-in list plus the user's additions, de-duplicated and sorted.
    /// </summary>
    public static IReadOnlyList<string> Effective(IEnumerable<string>? customGlobs)
    {
        var merged = new SortedSet<string>(DefaultGlobs, StringComparer.Ordinal);

        foreach (var glob in customGlobs ?? [])
        {
            var trimmed = glob.Trim();

            if (trimmed.Length > 0)
            {
                merged.Add(trimmed);
            }
        }

        return [.. merged];
    }

    /// <summary>
    /// Normalises what the user typed: trimmed, blank entries dropped, duplicates removed, and
    /// anything already in the built-in list discarded rather than stored twice.
    /// </summary>
    public static IReadOnlyList<string> NormaliseCustom(IEnumerable<string>? customGlobs)
    {
        var builtIn = new HashSet<string>(DefaultGlobs, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var glob in customGlobs ?? [])
        {
            var trimmed = glob.Trim();

            if (trimmed.Length > 0 && !builtIn.Contains(trimmed) && seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
