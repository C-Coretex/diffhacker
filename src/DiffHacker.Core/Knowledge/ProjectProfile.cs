namespace DiffHacker.Core.Knowledge;

/// <summary>
/// Everything stored about one repository: what a model wrote about it, what the user wrote
/// about it, and where the model's part came from.
/// <para>
/// The split down the middle is the point. <see cref="Document"/> is regenerated wholesale every
/// time the user asks for a new profile; <see cref="UserNotes"/>,
/// <see cref="CustomInstructions"/>, <see cref="CustomExcludedGlobs"/> and
/// <see cref="CharacterBudget"/> are the user's and are never touched by a run. Keeping them in
/// separate fields, saved through separate methods, means "manual edits survive regeneration" is
/// a property of the shape rather than a rule someone has to remember.
/// </para>
/// </summary>
public sealed record ProjectProfile
{
    /// <summary>Absolute, normalised worktree root. The key everything here is stored under.</summary>
    public required string RepositoryPath { get; init; }

    /// <summary>
    /// The model's answer, or null when nothing has been generated yet. The user's sections
    /// exist independently of it, which is why a profile with no document is a real thing.
    /// </summary>
    public ProjectProfileDocument? Document { get; init; }

    /// <summary>Where <see cref="Document"/> came from. Null exactly when the document is.</summary>
    public ProfileProvenance? Provenance { get; init; }

    /// <summary>The user's standing notes. Never rewritten by a run.</summary>
    public string UserNotes { get; init; } = string.Empty;

    /// <summary>
    /// Free text injected into every analysis prompt for this repository — "this is CQRS",
    /// "ignore the generated/ folder", "the Legacy project is being deleted".
    /// </summary>
    public string CustomInstructions { get; init; } = string.Empty;

    /// <summary>
    /// Globs the user added on top of <see cref="SensitiveFiles.DefaultGlobs"/>. Stored as the
    /// additions alone so that changing the built-in list does not silently rewrite the user's.
    /// </summary>
    public IReadOnlyList<string> CustomExcludedGlobs { get; init; } = [];

    /// <summary>
    /// Hard size budget for the rendered document, in characters, or null for the default. Per
    /// repository because it is paid on every analysis of that repository and a monorepo needs
    /// room a small service does not.
    /// </summary>
    public int? CharacterBudget { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>The budget actually in force.</summary>
    public int EffectiveCharacterBudget => CharacterBudget ?? ProfileBudget.DefaultCharacters;

    /// <summary>Everything withheld from the model for this repository, sorted.</summary>
    public IReadOnlyList<string> EffectiveExcludedGlobs => SensitiveFiles.Effective(CustomExcludedGlobs);

    /// <summary>
    /// A profile for a repository nothing has been stored about yet. Returned rather than null so
    /// that the screen, the toolbox and the analysis prompt all deal in one shape.
    /// </summary>
    public static ProjectProfile Empty(string repositoryPath, DateTimeOffset now) => new()
    {
        RepositoryPath = repositoryPath,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    };
}

/// <summary>
/// The generated half of a profile: standing knowledge about a repository, exactly as the model
/// produced it or as the user has since edited it.
/// <para>
/// Mirrors <c>schema/project-profile-document.schema.json</c>, which is the document the model is
/// asked for and validated against. This is the domain copy; the generated contract record is the
/// wire copy, and the Host maps between them.
/// </para>
/// </summary>
public sealed record ProjectProfileDocument
{
    /// <summary>What the project is and what problem it solves.</summary>
    public required string Purpose { get; init; }

    /// <summary>How the system is put together at a high level. Markdown.</summary>
    public required string Architecture { get; init; }

    /// <summary>Projects or modules, most worth understanding first.</summary>
    public IReadOnlyList<ProjectModule> Modules { get; init; } = [];

    /// <summary>Layering and dependency conventions. Markdown.</summary>
    public required string Layering { get; init; }

    /// <summary>Notable patterns, idioms and conventions. Markdown.</summary>
    public required string Patterns { get; init; }

    /// <summary>Where a reader starts.</summary>
    public IReadOnlyList<ProjectEntryPoint> EntryPoints { get; init; } = [];

    /// <summary>Where the tests live and how they are organised. Markdown.</summary>
    public required string TestLayout { get; init; }

    /// <summary>
    /// Repository documentation the profile was built from, in the order it was read. Recorded
    /// because "it read the README before it grepped anything" is otherwise unverifiable.
    /// </summary>
    public IReadOnlyList<string> DocumentationSources { get; init; } = [];
}

/// <summary>One project or module, and what it is for.</summary>
public sealed record ProjectModule
{
    public required string Name { get; init; }

    /// <summary>Repository-relative path of the module's root directory.</summary>
    public required string Path { get; init; }

    public required string Summary { get; init; }

    /// <summary>Names of the modules this one depends on or is most closely coupled to.</summary>
    public IReadOnlyList<string> RelatedModules { get; init; } = [];
}

/// <summary>One place a reader can start from.</summary>
public sealed record ProjectEntryPoint
{
    public required string Path { get; init; }

    public required string Purpose { get; init; }
}

/// <summary>
/// Where a generated document came from: which commit, when, and at whose expense.
/// <para>
/// The commit is the whole basis of drift detection, so it is recorded even though nothing else
/// in the application has ever needed a commit hash.
/// </para>
/// </summary>
public sealed record ProfileProvenance
{
    /// <summary>HEAD at generation time, or null in a repository with no commits.</summary>
    public string? CommitSha { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>Display name of the provider profile that produced it.</summary>
    public required string ProviderDisplayName { get; init; }

    public required string Model { get; init; }

    /// <summary>Length of the rendered document, measured against the budget.</summary>
    public required int DocumentCharacters { get; init; }
}
