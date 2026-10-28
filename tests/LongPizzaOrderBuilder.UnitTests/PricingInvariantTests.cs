using System.Text.Json;
using LongPizzaOrderBuilder.Pricing;
using Xunit;

namespace LongPizzaOrderBuilder.UnitTests;

/// <summary>
/// Invariant tests for the pricing engine (see docs/INVARIANTS.md; IDs appear in the Trait attributes).
///
/// They run a deterministic corpus of orders through <see cref="PricingService"/> and compare every result with an
/// independent oracle that works in integer cents. The oracle deliberately shares no code with the service, so a
/// mistake in the service's decimal arithmetic or rounding cannot also hide in the test.
/// </summary>
public sealed class PricingInvariantTests
{
    private static readonly string[] Promos = [null!, "ILOVEPIZZA", "SAVE5", "save5", "  ilovepizza  ", "BOGUS"];
    private static readonly TipRequest?[] Tips =
    [
        null, new("None"), new("Percent", 0m), new("Percent", 10m), new("Percent", 15m), new("Percent", 18m),
        new("Percent", 20m), new("Percent", 100m), new("Custom", null, 0m), new("Custom", null, 3.50m), new("Custom", null, 10000m),
    ];

    private static readonly string[] BasicToppings = ["pepperoni", "sausage", "extra-cheese", "onions", "black-olives", "green-peppers"];
    private static readonly string[] PremiumToppings = ["bacon", "chicken", "garlic", "artichoke-hearts", "sun-dried-tomatoes", "pineapple"];
    private static readonly string[] Sizes = ["Small", "Medium", "Large"];

    private sealed record Case(PricingPreviewRequest Request, PricingPreviewResponse Response);

    /// <summary>Computed once: a fixed-seed corpus, so failures are reproducible.</summary>
    private static readonly Lazy<IReadOnlyList<Case>> Corpus = new(BuildCorpus);

    private static IReadOnlyList<Case> BuildCorpus()
    {
        var pricing = new PricingService();
        var orders = new List<List<PizzaOrderItemRequest>>();

        // Exhaustive single-line orders: every size x representative topping sets x quantities.
        string[][] toppingSets = [[], ["pepperoni"], ["bacon"], ["pepperoni", "bacon"], [.. BasicToppings], [.. PremiumToppings], [.. BasicToppings, .. PremiumToppings]];
        foreach (var size in Sizes)
            foreach (var set in toppingSets)
                foreach (var quantity in new[] { 1, 2, 3, 10 })
                    orders.Add([new(size, "Classic", [.. set], quantity)]);

        // Seeded multi-line orders with random toppings (1 to 4 lines).
        var random = new Random(20261027);
        var all = BasicToppings.Concat(PremiumToppings).ToArray();
        for (var i = 0; i < 250; i++)
        {
            var lines = new List<PizzaOrderItemRequest>();
            for (var line = 0; line < random.Next(1, 5); line++)
            {
                var toppings = all.Where(_ => random.Next(3) == 0).ToList();
                lines.Add(new(Sizes[random.Next(Sizes.Length)], "Classic", toppings, random.Next(1, 11)));
            }
            orders.Add(lines);
        }

        var cases = new List<Case>();
        foreach (var items in orders)
            foreach (var promo in Promos)
                foreach (var tip in Tips)
                {
                    var request = new PricingPreviewRequest(items, promo, tip);
                    cases.Add(new Case(request, pricing.Calculate(request)));
                }
        return cases;
    }

    // ---------- independent oracle (integer cents; no decimal arithmetic) ----------

    /// <summary>Round-half-away-from-zero of numerator/denominator for non-negative values.</summary>
    private static long Round(long numerator, long denominator) => (2 * numerator + denominator) / (2 * denominator);
    private static long BaseCents(string size) => size switch { "Small" => 500, "Medium" => 700, _ => 1200 };
    private static long ToppingCents(string size, string id) => Round(BaseCents(size) * (BasicToppings.Contains(id) ? 8 : 11), 100);
    private static long Cents(decimal amount)
    {
        Assert.Equal(decimal.Truncate(amount * 100), amount * 100); // PRC-012: never more than 2 decimals
        return (long)(amount * 100);
    }

    private sealed record Expected(long Subtotal, long Discount, long Discounted, long Tax, long Tip, long Total, long[] LineTotals, long[] UnitPrices);

    private static Expected Oracle(PricingPreviewRequest request)
    {
        var unitPrices = request.Items.Select(i => BaseCents(i.Size) + i.Toppings.Sum(t => ToppingCents(i.Size, t))).ToArray();
        var lineTotals = request.Items.Select((i, n) => unitPrices[n] * i.Quantity).ToArray();
        var subtotal = lineTotals.Sum();
        var code = request.PromoCode?.Trim().ToUpperInvariant();
        var discount = code switch
        {
            "ILOVEPIZZA" => Round(subtotal * 10, 100),
            "SAVE5" when subtotal > 2000 => 500,
            _ => 0,
        };
        discount = Math.Min(discount, subtotal);
        var discounted = subtotal - discount;
        var tax = Round(discounted * 65, 1000); // 6.5%
        var tip = request.Tip?.Type switch
        {
            "Percent" => Round((discounted + tax) * (long)request.Tip.Percent!.Value, 100),
            "Custom" => (long)(request.Tip.Amount!.Value * 100),
            _ => 0,
        };
        return new Expected(subtotal, discount, discounted, tax, tip, discounted + tax + tip, lineTotals, unitPrices);
    }

    private static void ForEachCase(Action<Case, Expected> check)
    {
        foreach (var c in Corpus.Value)
        {
            Assert.True(c.Response.IsValid, $"Corpus order should be valid: {JsonSerializer.Serialize(c.Request)}");
            check(c, Oracle(c.Request));
        }
    }

    [Fact]
    public void Corpus_is_large_enough_to_mean_something()
    {
        Assert.True(Corpus.Value.Count > 20000, $"Corpus has only {Corpus.Value.Count} cases.");
    }

    [Fact, Trait("Invariant", "PRC-003")]
    public void Line_total_is_unit_price_times_quantity_and_unit_price_is_base_plus_toppings()
    {
        ForEachCase((c, expected) =>
        {
            var lines = c.Response.Breakdown!.Lines;
            Assert.Equal(expected.LineTotals.Length, lines.Count);
            for (var n = 0; n < lines.Count; n++)
            {
                Assert.Equal(expected.UnitPrices[n], Cents(lines[n].UnitPrice));
                Assert.Equal(expected.LineTotals[n], Cents(lines[n].LineTotal));
            }
        });
    }

    [Fact, Trait("Invariant", "PRC-004")]
    public void Subtotal_is_the_sum_of_line_totals()
    {
        ForEachCase((c, expected) =>
        {
            var breakdown = c.Response.Breakdown!;
            Assert.Equal(breakdown.Lines.Sum(l => Cents(l.LineTotal)), Cents(breakdown.Subtotal));
            Assert.Equal(expected.Subtotal, Cents(breakdown.Subtotal));
        });
    }

    [Fact, Trait("Invariant", "PRC-007")]
    public void An_unrecognised_or_ineligible_code_never_reduces_the_total()
    {
        ForEachCase((c, expected) =>
        {
            var breakdown = c.Response.Breakdown!;
            if (!c.Response.Promotion.Applied) Assert.Equal(0m, breakdown.Discount);
            if (c.Request.PromoCode?.Trim().ToUpperInvariant() == "BOGUS")
            {
                Assert.False(c.Response.Promotion.Applied);
                Assert.Equal(0m, breakdown.Discount);
            }
            Assert.Equal(c.Response.Promotion.Applied, expected.Discount > 0);
        });
    }

    [Fact, Trait("Invariant", "PRC-008")]
    public void Discount_is_never_negative_and_never_exceeds_the_subtotal()
    {
        ForEachCase((c, expected) =>
        {
            var breakdown = c.Response.Breakdown!;
            Assert.True(breakdown.Discount >= 0m);
            Assert.True(breakdown.Discount <= breakdown.Subtotal);
            Assert.Equal(expected.Discount, Cents(breakdown.Discount));
            Assert.Equal(expected.Discounted, Cents(breakdown.DiscountedSubtotal));
        });
    }

    [Fact, Trait("Invariant", "PRC-009")]
    public void Tax_is_charged_on_the_discounted_subtotal_and_never_on_the_tip()
    {
        ForEachCase((c, expected) => Assert.Equal(expected.Tax, Cents(c.Response.Breakdown!.Tax)));

        // Same order and promotion, different tips: the tax must not move.
        foreach (var group in Corpus.Value.GroupBy(c => JsonSerializer.Serialize(new { c.Request.Items, c.Request.PromoCode })))
            Assert.Single(group.Select(c => c.Response.Breakdown!.Tax).Distinct());
    }

    [Fact, Trait("Invariant", "PRC-010")]
    public void Tip_is_none_a_percentage_of_discounted_subtotal_plus_tax_or_the_custom_amount_as_entered()
    {
        ForEachCase((c, expected) =>
        {
            var tip = Cents(c.Response.Breakdown!.Tip);
            Assert.Equal(expected.Tip, tip);
            if (c.Request.Tip is null || c.Request.Tip.Type == "None") Assert.Equal(0L, tip);
            if (c.Request.Tip?.Type == "Custom") Assert.Equal(Cents(c.Request.Tip.Amount!.Value), tip);
        });
    }

    [Fact, Trait("Invariant", "PRC-011")]
    public void Total_is_exactly_discounted_subtotal_plus_tax_plus_tip()
    {
        ForEachCase((c, expected) =>
        {
            var b = c.Response.Breakdown!;
            Assert.Equal(b.DiscountedSubtotal + b.Tax + b.Tip, b.Total);
            Assert.Equal(b.Subtotal - b.Discount, b.DiscountedSubtotal);
            Assert.Equal(expected.Total, Cents(b.Total));
        });
    }

    [Fact, Trait("Invariant", "PRC-012")]
    public void Every_money_amount_has_at_most_two_decimals()
    {
        // Cents() asserts the two-decimal rule for each value it converts.
        ForEachCase((c, _) =>
        {
            var b = c.Response.Breakdown!;
            foreach (var amount in new[] { b.Subtotal, b.Discount, b.DiscountedSubtotal, b.Tax, b.Tip, b.Total })
                Cents(amount);
            foreach (var line in b.Lines)
            {
                Cents(line.BaseUnitPrice); Cents(line.UnitPrice); Cents(line.LineTotal);
                foreach (var topping in line.PricedToppings) Cents(topping.UnitPrice);
            }
        });
    }

    [Fact, Trait("Invariant", "PRC-012")]
    public void A_half_cent_tax_rounds_away_from_zero()
    {
        // A Small pizza is $5.00; 6.5% of that is exactly $0.325, which must round up to $0.33 (banker's rounding would give $0.32).
        var result = new PricingService().Calculate(new PricingPreviewRequest([new("Small", "Classic", [], 1)]));
        Assert.Equal(0.33m, result.Breakdown!.Tax);
    }

    [Fact, Trait("Invariant", "PRC-013")]
    public void Pricing_is_repeatable_and_leaves_the_request_untouched()
    {
        var pricing = new PricingService();
        foreach (var c in Corpus.Value.Where((_, n) => n % 37 == 0))
        {
            var before = JsonSerializer.Serialize(c.Request);
            var first = JsonSerializer.Serialize(pricing.Calculate(c.Request));
            var second = JsonSerializer.Serialize(pricing.Calculate(c.Request));
            Assert.Equal(first, second);
            Assert.Equal(before, JsonSerializer.Serialize(c.Request));
        }
    }

    [Fact, Trait("Invariant", "PRC-014")]
    public void Invalid_orders_are_rejected_with_reasons_and_never_discounted()
    {
        var pricing = new PricingService();
        PizzaOrderItemRequest[][] invalidOrders =
        [
            [new("XL", "Classic", [], 1)],
            [new("Small", "Stuffed", [], 1)],
            [new("Small", "Classic", [], 0)],
            [new("Small", "Classic", [], -1)],
            [new("Small", "Classic", [], 11)],
            [new("Small", "Classic", ["pepperoni", "pepperoni"], 1)],
            [new("Small", "Classic", ["unobtainium"], 1)],
            [new("Large", "Classic", [], 1), new("Small", "Classic", ["pepperoni", "PEPPERONI"], 1)],
            [null!],
            [new("Small", "Classic", null!, 1)],
        ];
        foreach (var items in invalidOrders)
            foreach (var promo in new[] { "ILOVEPIZZA", "SAVE5", null })
            {
                var result = pricing.Calculate(new PricingPreviewRequest([.. items], promo));
                Assert.False(result.IsValid);
                Assert.Null(result.Breakdown);
                Assert.True(result.Errors.Count > 0);
                Assert.False(result.Promotion.Applied);
                Assert.Equal(0m, result.Promotion.DiscountAmount);
            }

        foreach (var tip in new TipRequest?[] { new("Percent", -1m), new("Percent", 101m), new("Percent"), new("Custom", null, -0.01m), new("Custom", null, 10000.01m), new("Custom"), new("Bogus"), new("") })
        {
            var result = pricing.Calculate(new PricingPreviewRequest([new("Small", "Classic", [], 1)], null, tip));
            Assert.False(result.IsValid);
            Assert.Null(result.Breakdown);
        }

        Assert.False(pricing.Calculate(new PricingPreviewRequest([])).IsValid); // an empty order cannot be priced (ORD-001)
    }

    [Fact, Trait("Invariant", "PRC-014")]
    public void An_order_may_not_have_more_than_fifty_pizza_lines()
    {
        var pricing = new PricingService();
        PizzaOrderItemRequest Line() => new("Small", "Classic", [], 1);
        Assert.True(pricing.Calculate(new PricingPreviewRequest([.. Enumerable.Range(0, 50).Select(_ => Line())])).IsValid);
        var tooMany = pricing.Calculate(new PricingPreviewRequest([.. Enumerable.Range(0, 51).Select(_ => Line())]));
        Assert.False(tooMany.IsValid);
        Assert.Null(tooMany.Breakdown);
        Assert.Contains(tooMany.Errors, e => e.Contains("at most 50"));
    }

    [Fact, Trait("Invariant", "PRC-015")]
    public void Changing_the_menu_data_changes_prices_without_changing_code()
    {
        // A different menu: Large is $20, pepperoni is 10%, tax is 10%, SAVE5 is $3 off orders over $30.
        var custom = new PizzaMenu(
            [new("Small", 5.00m), new("Medium", 7.00m), new("Large", 20.00m)],
            ["Classic"],
            [new("pepperoni", "Pepperoni", "Basic", 0.10m)],
            0.10m,
            [new("SAVE5", "test", "FixedAmount", null, 3.00m, 30.00m)]);
        var pricing = new PricingService(custom);

        var one = pricing.Calculate(new PricingPreviewRequest([new("Large", "Classic", ["pepperoni"], 1)], "SAVE5"));
        Assert.Equal(22.00m, one.Breakdown!.Subtotal);   // 20.00 + 10% of 20.00
        Assert.False(one.Promotion.Applied);              // 22.00 is not over this menu's $30 threshold
        Assert.Equal(2.20m, one.Breakdown.Tax);
        Assert.Equal(24.20m, one.Breakdown.Total);

        var two = pricing.Calculate(new PricingPreviewRequest([new("Large", "Classic", ["pepperoni"], 2)], "SAVE5"));
        Assert.Equal(44.00m, two.Breakdown!.Subtotal);
        Assert.True(two.Promotion.Applied);
        Assert.Equal(3.00m, two.Breakdown.Discount);
        Assert.Equal(4.10m, two.Breakdown.Tax);
        Assert.Equal(45.10m, two.Breakdown.Total);

        // A code that only exists in the default menu is unknown here.
        Assert.False(pricing.Calculate(new PricingPreviewRequest([new("Large", "Classic", [], 2)], "ILOVEPIZZA")).Promotion.Applied);
    }

    [Fact, Trait("Invariant", "PRC-015")]
    public void A_new_size_crust_topping_and_promotion_work_from_menu_data_alone()
    {
        var menu = PizzaMenu.Default with
        {
            Sizes = [.. PizzaMenu.Default.Sizes, new("Party", 20.00m)],
            Crusts = [.. PizzaMenu.Default.Crusts, "Stuffed"],
            Toppings = [.. PizzaMenu.Default.Toppings, new("truffle", "Truffle", "Luxury", 0.25m)],
            Promotions = [.. PizzaMenu.Default.Promotions, new("TWENTY", "20% off orders over $10", "Percentage", 0.20m, null, 10.00m)],
        };
        var result = new PricingService(menu).Calculate(
            new PricingPreviewRequest([new("Party", "Stuffed", ["truffle", "pepperoni"], 1)], "twenty"));

        Assert.True(result.IsValid);
        Assert.Equal(26.60m, result.Breakdown!.Subtotal);   // 20.00 + 25% of 20.00 (truffle) + 8% of 20.00 (pepperoni)
        Assert.True(result.Promotion.Applied);
        Assert.Equal(5.32m, result.Breakdown.Discount);     // 20% of 26.60
        Assert.Equal(21.28m, result.Breakdown.DiscountedSubtotal);
        Assert.Equal(1.38m, result.Breakdown.Tax);          // 6.5% of 21.28 = 1.3832
        Assert.Equal(22.66m, result.Breakdown.Total);
    }
}
