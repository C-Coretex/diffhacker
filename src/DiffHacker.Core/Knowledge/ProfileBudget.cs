namespace DiffHacker.Core.Knowledge;

/// <summary>
/// The hard size budget on a generated profile.
/// <para>
/// The profile is not paid for once. It goes into the prompt of every analysis of this repository,
/// and that prompt is re-sent on every turn of a tool-using run, so a profile that is twice as
/// long costs twice as much on every future analysis rather than once here. That is what makes
/// this a budget rather than a style guide.
/// </para>
/// <para>
/// Measured in characters, not tokens: character counting is exact, free and identical across
/// every provider, where a token count would need a tokeniser per model to be anything better
/// than a guess. Roughly four characters to the token, so the default is about 3,000 tokens.
/// </para>
/// <para>
/// The user's own notes and custom instructions sit outside the budget. They are paid for on the
/// same terms, and their size is reported for that reason, but capping what the user chose to
/// write is not the application's call.
/// </para>
/// </summary>
public static class ProfileBudget
{
    /// <summary>
    /// 12,000 characters, about 3,000 tokens. Large enough for a real module map on a large
    /// repository — the failure this guards against in the other direction is a profile so terse
    /// it says nothing a reviewer could not have guessed.
    /// </summary>
    public const int DefaultCharacters = 30_000;

    /// <summary>Floor and ceiling on a user-set budget, matched by the request schema.</summary>
    public const int MinimumCharacters = 1_000;

    /// <summary>See <see cref="MinimumCharacters"/>.</summary>
    public const int MaximumCharacters = 200_000;

    /// <summary>
    /// The size of the generated document as it will actually be spent: the rendered text, not
    /// the JSON around it. Measuring the JSON would charge the model for quotation marks it never
    /// sends to the next run.
    /// </summary>
    public static int Measure(ProjectProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ProfileTextRenderer.RenderDocument(document).Length;
    }

    /// <summary>Clamps a user-supplied budget into the supported range.</summary>
    public static int Clamp(int? requested) =>
        requested is null
            ? DefaultCharacters
            : Math.Clamp(requested.Value, MinimumCharacters, MaximumCharacters);
}
