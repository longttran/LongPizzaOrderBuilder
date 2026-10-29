using LongPizzaOrderBuilder.OrderService;
using LongPizzaOrderBuilder.Pricing;
using Xunit;

namespace LongPizzaOrderBuilder.UnitTests;

/// <summary>Invariants about what the order service accepts and what it charges (ORD-001, ORD-002).</summary>
public sealed class OrderInvariantTests
{
    private readonly PricingService _pricing = new();

    [Fact, Trait("Invariant", "ORD-001")]
    public void An_order_with_no_valid_pizza_is_never_accepted()
    {
        var store = new OrderStore(_pricing);
        Assert.Throws<PricingValidationException>(() => store.Create(new OrderRequest("Sam", [], null, null), "a"));
        Assert.Throws<PricingValidationException>(() => store.Create(new OrderRequest("Sam", [new("Small", "Classic", [], 0)], null, null), "b"));
        Assert.Throws<PricingValidationException>(() => store.Create(new OrderRequest("Sam", [new("Small", "Classic", ["unobtainium"], 1)], null, null), "c"));
        Assert.Empty(store.All());
    }

    [Fact, Trait("Invariant", "ORD-002")]
    public void The_accepted_price_is_exactly_what_the_pricing_code_gives_for_the_same_choices()
    {
        // The order builder previews with PricingService, and the order service prices with the same PricingService. Both must agree to the cent.
        var store = new OrderStore(_pricing);
        var count = 0;
        foreach (var size in new[] { "Small", "Medium", "Large" })
            foreach (var toppings in new[] { Array.Empty<string>(), ["pepperoni"], ["bacon", "pepperoni", "pineapple"] })
                foreach (var promo in new string?[] { null, "ILOVEPIZZA", "SAVE5", "BOGUS" })
                    foreach (var tip in new TipRequest?[] { null, new("Percent", 18m), new("Custom", null, 3.50m) })
                        foreach (var quantity in new[] { 1, 4 })
                        {
                            var items = new List<PizzaOrderItemRequest> { new(size, "Classic", [.. toppings], quantity), new("Small", "Thin", [], 1) };
                            var preview = _pricing.Calculate(new PricingPreviewRequest(items, promo, tip)).Breakdown!;
                            var order = store.Create(new OrderRequest("Sam", items, promo, tip), $"key-{count++}").Order;
                            Assert.Equal(preview.Total, order.Total);
                            Assert.Equal(preview.Subtotal, order.Pricing.Subtotal);
                            Assert.Equal(preview.Discount, order.Pricing.Discount);
                            Assert.Equal(preview.Tax, order.Pricing.Tax);
                            Assert.Equal(preview.Tip, order.Pricing.Tip);
                        }
        Assert.True(count > 100);
    }

    [Fact, Trait("Invariant", "ORD-002")]
    public void An_invalid_promotion_does_not_change_the_accepted_total()
    {
        var store = new OrderStore(_pricing);
        var items = new List<PizzaOrderItemRequest> { new("Medium", "Classic", ["pepperoni"], 1) };
        var withBogus = store.Create(new OrderRequest("A", items, "BOGUS", null), "x");
        var without = store.Create(new OrderRequest("B", items, null, null), "y");
        Assert.Equal(without.Order.Total, withBogus.Order.Total);
    }
}
