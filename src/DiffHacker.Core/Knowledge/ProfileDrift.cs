namespace DiffHacker.Core.Knowledge;

/// <summary>
/// How far the repository has moved since the profile was generated.
/// <para>
/// Measured in files rather than in commits or in days. Commits would be the natural unit, but
/// counting them needs <c>git rev-list</c>, which is not on the read-only allowlist and would be a
/// deliberate widening of the product's contract to obtain a worse signal: a hundred typo commits
/// drift a repository less than one that moves a module. Days are worse still — a repository
/// nobody touched for a year has not drifted at all.
/// </para>
/// </summary>
public readonly record struct ProfileDrift
{
    /// <summary>Files differing between the profile's commit and HEAD, when that could be measured.</summary>
    public int? FilesChanged { get; init; }

    /// <summary>Files git currently tracks — the denominator.</summary>
    public int? TrackedFiles { get; init; }

    /// <summary>
    /// False when the profile's commit is no longer in the repository: a rebase, a history
    /// rewrite, a shallow clone. The profile is then of unknown age rather than of a measurable
    /// one, which is itself a reason to offer regeneration.
    /// </summary>
    public bool? CommitReachable { get; init; }

    /// <summary>Whether the user should be prompted to regenerate.</summary>
    public bool IsSubstantial { get; init; }

    /// <summary>Nothing to compare against: no profile, or no commit it was taken from.</summary>
    public static ProfileDrift Unknown => default;
}

/// <summary>
/// The rule behind <see cref="ProfileDrift.IsSubstantial"/>, in one place so the threshold is a
/// decision rather than a scattering of magic numbers.
/// </summary>
public static class ProfileDriftCalculator
{
    /// <summary>
    /// Above this many changed files the repository has drifted whatever its size. A thousand-file
    /// repository and a hundred-thousand-file one both stop resembling their profile somewhere
    /// around here.
    /// </summary>
    public const int AbsoluteFileThreshold = 150;

    /// <summary>
    /// And below that, a tenth of the repository. The proportional rule is what catches a small
    /// project where fifty files is everything.
    /// </summary>
    public const int ProportionalDivisor = 10;

    /// <summary>
    /// Judges a comparison. An unreachable commit is substantial drift by definition: the honest
    /// answer is "we cannot tell how stale this is", and offering regeneration is the right
    /// response to that.
    /// </summary>
    public static ProfileDrift Evaluate(int filesChanged, int trackedFiles, bool commitReachable)
    {
        if (!commitReachable)
        {
            return new ProfileDrift
            {
                TrackedFiles = trackedFiles,
                CommitReachable = false,
                IsSubstantial = true,
            };
        }

        var substantial =
            filesChanged > AbsoluteFileThreshold ||
            (trackedFiles > 0 && filesChanged > trackedFiles / ProportionalDivisor);

        return new ProfileDrift
        {
            FilesChanged = filesChanged,
            TrackedFiles = trackedFiles,
            CommitReachable = true,
            IsSubstantial = substantial,
        };
    }
}
