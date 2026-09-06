using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using DiffHacker.Llm.Pricing;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// Requirement 3's last clause: an estimated cost <b>where pricing is known</b>.
/// <para>
/// The emphasis is the point. The bundled table is a snapshot and it will go stale, so the
/// behaviour that matters most here is what happens when it has never heard of a model: no
/// price, stated as such. A zero would read as "this was free" and a neighbouring model's rate
/// would read as authoritative — both are worse than an honest blank in a screen whose whole
/// job is making cost predictable.
/// </para>
/// </summary>
public sealed class ModelPricingTests
{
    private static readonly ModelPricing Pricing = new();

    [Fact]
    public void The_bundled_table_loads_and_says_how_old_it_is()
    {
        Pricing.TableAsOf.Year.ShouldBeGreaterThanOrEqualTo(
            2025,
            "the date is shown beside every estimate, so a reader can judge how much to trust it.");
    }

    [Theory]
    [InlineData(LlmProviderType.OpenAi, "gpt-4o")]
    [InlineData(LlmProviderType.Anthropic, "claude-sonnet-4-5")]
    [InlineData(LlmProviderType.Gemini, "gemini-2.5-pro")]
    [InlineData(LlmProviderType.Grok, "grok-4")]
    [InlineData(LlmProviderType.DeepSeek, "deepseek-chat")]
    public void A_known_model_has_a_rate(LlmProviderType type, string model)
    {
        Pricing.TryGetRate(type, model, out var rate).ShouldBeTrue();

        rate.InputPerMillion.ShouldBeGreaterThan(0);
        rate.OutputPerMillion.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_dated_model_identifier_resolves_to_its_family()
    {
        // Providers pin snapshots: users type gpt-4o-2024-08-06 and expect a price.
        Pricing.TryGetRate(LlmProviderType.OpenAi, "gpt-4o-2024-08-06", out var rate).ShouldBeTrue();
        rate.InputPerMillion.ShouldBe(2.50m);
    }

    [Fact]
    public void The_longer_prefix_wins()
    {
        // gpt-4o-mini must not be priced as gpt-4o. This is the whole reason lookup is
        // longest-prefix rather than first-match.
        Pricing.TryGetRate(LlmProviderType.OpenAi, "gpt-4o-mini", out var mini).ShouldBeTrue();
        Pricing.TryGetRate(LlmProviderType.OpenAi, "gpt-4o", out var full).ShouldBeTrue();

        mini.InputPerMillion.ShouldBeLessThan(full.InputPerMillion);
    }

    [Fact]
    public void An_unknown_model_has_no_rate_rather_than_a_neighbours()
    {
        Pricing.TryGetRate(LlmProviderType.OpenAi, "some-model-nobody-has-heard-of", out _)
            .ShouldBeFalse("a plausible-looking wrong number is worse than an honest blank.");
    }

    [Fact]
    public void An_arbitrary_compatible_endpoint_has_no_prices_at_all()
    {
        // Correct: nobody can know what a user's own gateway charges.
        Pricing.TryGetRate(LlmProviderType.OpenAiCompatible, "llama3.1", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_model_on_the_wrong_provider_is_not_priced()
    {
        Pricing.TryGetRate(LlmProviderType.Anthropic, "gpt-4o", out _).ShouldBeFalse();
    }

    [Fact]
    public void Cost_is_the_two_rates_applied_to_the_two_counts()
    {
        var rate = new LlmModelRate { InputPerMillion = 3m, OutputPerMillion = 15m };

        var usage = new LlmUsage { InputTokens = 2_000_000, OutputTokens = 1_000_000 };

        rate.CostOf(usage).ShouldBe(21m);
    }

    [Fact]
    public void Cached_input_is_billed_at_its_own_rate_and_not_twice()
    {
        // Cached tokens are a subset of the input count, not additional to it. Adding them
        // would overstate every cached run.
        var rate = new LlmModelRate
        {
            InputPerMillion = 10m,
            OutputPerMillion = 0m,
            CachedInputPerMillion = 1m,
        };

        var usage = new LlmUsage
        {
            InputTokens = 1_000_000,
            CachedInputTokens = 900_000,
        };

        rate.CostOf(usage).ShouldBe(1.9m);
    }

    [Fact]
    public void A_provider_that_does_not_price_the_cache_bills_it_as_input()
    {
        var rate = new LlmModelRate { InputPerMillion = 10m, OutputPerMillion = 0m };

        var usage = new LlmUsage { InputTokens = 1_000_000, CachedInputTokens = 900_000 };

        rate.CostOf(usage).ShouldBe(10m);
    }

    [Fact]
    public void An_override_needs_both_halves_to_count()
    {
        // A price with only one side would silently bill output at zero, which is exactly the
        // sort of wrong number this whole file exists to avoid.
        var half = SessionHarness.ProfileFor(LlmProviderType.OpenAi) with { InputCostPerMillion = 5m };
        half.CostOverride.ShouldBeNull();

        var whole = half with { OutputCostPerMillion = 10m };
        whole.CostOverride.ShouldNotBeNull();
        whole.CostOverride!.Value.OutputPerMillion.ShouldBe(10m);
    }

    [Fact]
    public void Usage_adds_up_across_requests()
    {
        var first = new LlmUsage { InputTokens = 10, OutputTokens = 2, IsReported = true, EstimatedCostUsd = 1m };
        var second = new LlmUsage { InputTokens = 5, OutputTokens = 1, IsReported = true, EstimatedCostUsd = 2m };

        var total = first + second;

        total.InputTokens.ShouldBe(15);
        total.TotalTokens.ShouldBe(18);
        total.EstimatedCostUsd.ShouldBe(3m);
        (LlmUsage.None + first).ShouldBe(first, "None must be a usable starting point.");
    }

    [Fact]
    public void A_provider_that_reported_nothing_is_not_recorded_as_free()
    {
        LlmUsage.None.IsReported.ShouldBeFalse();
        LlmUsage.None.CostIsKnown.ShouldBeFalse();
    }
}
