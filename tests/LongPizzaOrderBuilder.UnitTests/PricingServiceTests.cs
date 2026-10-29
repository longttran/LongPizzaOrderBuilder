using LongPizzaOrderBuilder.Pricing;
using Xunit;

namespace LongPizzaOrderBuilder.UnitTests;

public sealed class PricingServiceTests
{
    private readonly PricingService _pricing = new();

    [Theory]
    [Trait("Invariant", "PRC-001")]
    [InlineData("Small", 5.00)]
    [InlineData("Medium", 7.00)]
    [InlineData("Large", 12.00)]
    public void Uses_agreed_base_prices(string size, decimal expected)
    {
        var result = Preview(new PizzaOrderItemRequest(size, "Classic", [], 1));
        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Breakdown!.Subtotal);
    }

    [Fact]
    [Trait("Invariant", "PRC-002")]
    public void Prices_basic_and_premium_toppings_as_percentage_of_base_price()
    {
        var result = Preview(new PizzaOrderItemRequest("Small", "Classic", ["pepperoni", "bacon"], 1));
        Assert.True(result.IsValid);
        Assert.Equal(0.40m, result.Breakdown!.Lines[0].PricedToppings.Single(t => t.Id == "pepperoni").UnitPrice);
        Assert.Equal(0.55m, result.Breakdown.Lines[0].PricedToppings.Single(t => t.Id == "bacon").UnitPrice);
        Assert.Equal(5.95m, result.Breakdown.Subtotal);
    }

    [Fact]
    [Trait("Invariant", "PRC-005")]
    public void Save5_does_not_apply_at_exactly_twenty_dollars()
    {
        var result = _pricing.Calculate(new PricingPreviewRequest(
            [new("Small", "Classic", [], 4)], "SAVE5"));
        Assert.True(result.IsValid);
        Assert.Equal(20m, result.Breakdown!.Subtotal);
        Assert.False(result.Promotion.Applied);
        Assert.Equal(0m, result.Breakdown.Discount);
    }

    [Fact]
    [Trait("Invariant", "PRC-005")]
    public void Save5_applies_at_over_twenty_dollars()
    {
        var result = _pricing.Calculate(new PricingPreviewRequest(
            [new("Large", "Classic", [], 1), new("Large", "Thin", [], 1)], "SAVE5"));
        Assert.True(result.IsValid);
        Assert.Equal(24m, result.Breakdown!.Subtotal);
        Assert.True(result.Promotion.Applied);
        Assert.Equal(5m, result.Breakdown.Discount);
    }

    [Fact]
    [Trait("Invariant", "PRC-006")]
    public void Ilovepizza_discount_is_applied_before_tax()
    {
        var result = _pricing.Calculate(new PricingPreviewRequest(
            [new("Small", "Classic", [], 1)],
            "ILOVEPIZZA",
            new TipRequest("Percent", 10m)));
        Assert.True(result.IsValid);
        Assert.Equal(0.50m, result.Breakdown!.Discount);
        Assert.Equal(4.50m, result.Breakdown.DiscountedSubtotal);
        Assert.Equal(0.29m, result.Breakdown.Tax);
        Assert.Equal(0.48m, result.Breakdown.Tip);
        Assert.Equal(5.27m, result.Breakdown.Total);
    }

    [Fact]
    [Trait("Invariant", "PRC-010")]
    public void Percentage_tip_is_based_on_discounted_post_tax_amount()
    {
        var result = _pricing.Calculate(new PricingPreviewRequest(
            [new("Small", "Classic", [], 1)], null, new TipRequest("Percent", 10m)));
        Assert.True(result.IsValid);
        Assert.Equal(0.33m, result.Breakdown!.Tax);
        Assert.Equal(0.53m, result.Breakdown.Tip);
        Assert.Equal(5.86m, result.Breakdown.Total);
    }


    [Fact]
    [Trait("Invariant", "PRC-010")]
    public void Percentage_tip_is_based_on_discounted_post_tax_amount_after_discount()
    {
        var result = _pricing.Calculate(new PricingPreviewRequest(
            [new("Small", "Classic", [], 1)], "ILOVEPIZZA", new TipRequest("Percent", 10m)));
        Assert.True(result.IsValid);
        Assert.Equal(0.50m, result.Breakdown!.Discount);
        Assert.Equal(4.50m, result.Breakdown.DiscountedSubtotal);
        Assert.Equal(0.29m, result.Breakdown.Tax);
        Assert.Equal(0.48m, result.Breakdown.Tip);
        Assert.Equal(5.27m, result.Breakdown.Total);
    }

    [Fact]
    [Trait("Invariant", "PRC-014")]
    public void Rejects_invalid_sizes_toppings_duplicate_toppings_and_quantities()
    {
        var invalid = _pricing.Calculate(new PricingPreviewRequest(
            [new("Huge", "Classic", ["pepperoni", "PEPPERONI", "unlisted"], 11)]));
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, e => e.Contains("valid pizza size"));
        Assert.Contains(invalid.Errors, e => e.Contains("quantity"));
        Assert.Contains(invalid.Errors, e => e.Contains("selected once"));
        Assert.Contains(invalid.Errors, e => e.Contains("toppings are invalid"));
    }

    [Fact]
    [Trait("Invariant", "PRC-012")]
    public void Rounds_money_away_from_zero_to_two_decimal_places()
    {
        // Previously this asserted on Math.Round itself, which could never catch a regression in the service.
        // A Small pizza is $5.00 and 6.5% of that is exactly $0.325: away-from-zero gives $0.33 (banker's rounding would give $0.32).
        var result = _pricing.Calculate(new PricingPreviewRequest([new("Small", "Classic", [], 1)]));
        Assert.True(result.IsValid);
        Assert.Equal(0.33m, result.Breakdown!.Tax);
        Assert.Equal(5.33m, result.Breakdown.Total);
    }

    [Fact]
    [Trait("Invariant", "PRC-007")]
    public void Unknown_promotion_is_not_applied_and_explains_why()
    {
        var result = _pricing.Calculate(new PricingPreviewRequest([new("Small", "Classic", [], 1)], "NOTREAL"));
        Assert.True(result.IsValid);
        Assert.False(result.Promotion.Applied);
        Assert.Contains("not recognized", result.Promotion.Message);
        Assert.Equal(5m, result.Breakdown!.Subtotal);
    }

    private PricingPreviewResponse Preview(PizzaOrderItemRequest item) =>
        _pricing.Calculate(new PricingPreviewRequest([item]));
}
