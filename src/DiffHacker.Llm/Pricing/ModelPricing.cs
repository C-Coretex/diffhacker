using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;

namespace DiffHacker.Llm.Pricing;

/// <summary>
/// <see cref="ITokenPricing"/> over the price table bundled with the application.
/// <para>
/// The table is a snapshot, and it is treated as one: a model it has never heard of has no
/// price, full stop. The alternative — falling back to a similar-looking model, or to zero —
/// produces a number that looks authoritative and is not, which is worse than admitting
/// ignorance in a screen whose whole job is making cost predictable.
/// </para>
/// <para>
/// Lookup is exact first, then the longest matching prefix, so a table carrying both
/// <c>claude-sonnet-4</c> and <c>claude-sonnet-4-5</c> prices the latter correctly, and a dated
/// identifier like <c>gpt-4o-2024-08-06</c> still resolves.
/// </para>
/// </summary>
public sealed class ModelPricing : ITokenPricing
{
    private const string ResourceName = "DiffHacker.Llm.Pricing.model-prices.json";

    private readonly FrozenDictionary<LlmProviderType, ModelRateTable> _tables;

    public ModelPricing()
        : this(ReadBundledTable())
    {
    }

    internal ModelPricing(string json)
    {
        var document = JsonSerializer.Deserialize<PriceDocument>(json, SerializerOptions)
            ?? throw new InvalidOperationException("The bundled price table is empty.");

        TableAsOf = DateOnly.ParseExact(document.AsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Driven from the enum rather than from the file's keys, so a typo in the table costs
        // one provider its prices instead of failing the whole application at startup.
        _tables = Enum.GetValues<LlmProviderType>()
            .Where(type => document.Models.ContainsKey(ProviderTypeNames.ToStorage(type)))
            .ToFrozenDictionary(
                type => type,
                type => new ModelRateTable(document.Models[ProviderTypeNames.ToStorage(type)]));
    }

    public DateOnly TableAsOf { get; }

    public bool TryGetRate(LlmProviderType providerType, string model, out LlmModelRate rate)
    {
        rate = default;

        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return _tables.TryGetValue(providerType, out var table) && table.TryGet(model, out rate);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static string ReadBundledTable()
    {
        using var stream = typeof(ModelPricing).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The bundled price table '{ResourceName}' is missing from the assembly.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>One provider's models, indexed for exact-then-longest-prefix lookup.</summary>
    private sealed class ModelRateTable
    {
        private readonly FrozenDictionary<string, LlmModelRate> _exact;
        private readonly string[] _byDescendingLength;

        public ModelRateTable(Dictionary<string, PriceEntry> entries)
        {
            _exact = entries.ToFrozenDictionary(
                entry => entry.Key,
                entry => new LlmModelRate
                {
                    InputPerMillion = entry.Value.In,
                    OutputPerMillion = entry.Value.Out,
                    CachedInputPerMillion = entry.Value.CachedIn,
                },
                StringComparer.OrdinalIgnoreCase);

            _byDescendingLength =
            [
                .. entries.Keys
                    .OrderByDescending(key => key.Length)
                    .ThenBy(key => key, StringComparer.Ordinal),
            ];
        }

        public bool TryGet(string model, out LlmModelRate rate)
        {
            if (_exact.TryGetValue(model, out rate))
            {
                return true;
            }

            foreach (var candidate in _byDescendingLength)
            {
                if (model.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    rate = _exact[candidate];
                    return true;
                }
            }

            return false;
        }
    }

    private sealed record PriceDocument
    {
        [JsonPropertyName("asOf")]
        public required string AsOf { get; init; }

        [JsonPropertyName("models")]
        public Dictionary<string, Dictionary<string, PriceEntry>> Models { get; init; } = [];
    }

    private sealed record PriceEntry
    {
        [JsonPropertyName("in")]
        public decimal In { get; init; }

        [JsonPropertyName("out")]
        public decimal Out { get; init; }

        [JsonPropertyName("cachedIn")]
        public decimal? CachedIn { get; init; }
    }
}
