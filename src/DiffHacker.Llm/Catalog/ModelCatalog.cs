using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;

namespace DiffHacker.Llm.Catalog;

/// <summary>
/// <see cref="IModelCatalog"/> over the model table bundled with the application.
/// <para>
/// The table is a snapshot, and it is treated as one: a model it has never heard of has no
/// price and no context window, full stop. The alternative — falling back to a similar-looking
/// model, or to zero — produces a number that looks authoritative and is not, which is worse than
/// admitting ignorance in a screen whose whole job is making a run predictable.
/// </para>
/// <para>
/// Lookup is exact first, then the longest matching prefix, so a table carrying both
/// <c>claude-sonnet-4</c> and <c>claude-sonnet-4-5</c> prices the latter correctly, and a dated
/// identifier like <c>gpt-4o-2024-08-06</c> still resolves.
/// </para>
/// </summary>
public sealed class ModelCatalog : IModelCatalog
{
    private const string ResourceName = "DiffHacker.Llm.Catalog.model-catalog.json";

    private readonly FrozenDictionary<LlmProviderType, ModelEntryTable> _tables;

    public ModelCatalog()
        : this(ReadBundledTable())
    {
    }

    internal ModelCatalog(string json)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(json, SerializerOptions)
            ?? throw new InvalidOperationException("The bundled model table is empty.");

        TableAsOf = DateOnly.ParseExact(document.AsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Driven from the enum rather than from the file's keys, so a typo in the table costs
        // one provider its prices instead of failing the whole application at startup.
        _tables = Enum.GetValues<LlmProviderType>()
            .Where(type => document.Models.ContainsKey(ProviderTypeNames.ToStorage(type)))
            .ToFrozenDictionary(
                type => type,
                type => new ModelEntryTable(document.Models[ProviderTypeNames.ToStorage(type)]));
    }

    public DateOnly TableAsOf { get; }

    public bool TryGetRate(LlmProviderType providerType, string model, out LlmModelRate rate)
    {
        rate = default;

        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        if (!_tables.TryGetValue(providerType, out var table) || !table.TryGet(model, out var entry))
        {
            return false;
        }

        rate = entry.Rate;
        return true;
    }

    public bool TryGetContextWindow(LlmProviderType providerType, string model, out int tokens)
    {
        tokens = 0;

        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        // Two independent facts from one row. A model that is priced here but carries no "context"
        // of its own has an unknown window, not the window of whichever entry the prefix match
        // landed on — TryGetRate answering true says nothing about this question.
        if (!_tables.TryGetValue(providerType, out var table)
            || !table.TryGet(model, out var entry)
            || entry.ContextWindowTokens is not { } window)
        {
            return false;
        }

        tokens = window;
        return true;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static string ReadBundledTable()
    {
        using var stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The bundled price table '{ResourceName}' is missing from the assembly.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>What one row of the table says about one model.</summary>
    private readonly record struct ModelEntry(LlmModelRate Rate, int? ContextWindowTokens);

    /// <summary>One provider's models, indexed for exact-then-longest-prefix lookup.</summary>
    private sealed class ModelEntryTable
    {
        private readonly FrozenDictionary<string, ModelEntry> _exact;
        private readonly string[] _byDescendingLength;

        public ModelEntryTable(Dictionary<string, CatalogEntry> entries)
        {
            _exact = entries.ToFrozenDictionary(
                entry => entry.Key,
                entry => new ModelEntry(
                    new LlmModelRate
                    {
                        InputPerMillion = entry.Value.In,
                        OutputPerMillion = entry.Value.Out,
                        CachedInputPerMillion = entry.Value.CachedIn,
                    },
                    entry.Value.Context),
                StringComparer.OrdinalIgnoreCase);

            _byDescendingLength =
            [
                .. entries.Keys
                    .OrderByDescending(key => key.Length)
                    .ThenBy(key => key, StringComparer.Ordinal),
            ];
        }

        public bool TryGet(string model, out ModelEntry entry)
        {
            if (_exact.TryGetValue(model, out entry))
            {
                return true;
            }

            foreach (var candidate in _byDescendingLength)
            {
                if (model.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    entry = _exact[candidate];
                    return true;
                }
            }

            return false;
        }
    }

    private sealed record CatalogDocument
    {
        [JsonPropertyName("asOf")]
        public required string AsOf { get; init; }

        [JsonPropertyName("models")]
        public Dictionary<string, Dictionary<string, CatalogEntry>> Models { get; init; } = [];
    }

    private sealed record CatalogEntry
    {
        [JsonPropertyName("in")]
        public decimal In { get; init; }

        [JsonPropertyName("out")]
        public decimal Out { get; init; }

        [JsonPropertyName("cachedIn")]
        public decimal? CachedIn { get; init; }

        /// <summary>
        /// Context window in tokens. Null where the table does not know it, which is a different
        /// claim from the model having a small one.
        /// </summary>
        [JsonPropertyName("context")]
        public int? Context { get; init; }
    }
}
