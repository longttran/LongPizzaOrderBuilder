namespace LongPizzaOrderBuilder.Pricing;

/// <summary>
/// The menu as data: sizes, crusts, toppings, tax rate and promotion rules. Pricing reads everything from here (ADR-0005).
/// </summary>
public sealed record PizzaSizePrice(string Name, decimal Price);
public sealed record PizzaTopping(string Id, string Name, string Category, decimal Rate);
public sealed record PizzaMenu(
    IReadOnlyList<PizzaSizePrice> Sizes,
    IReadOnlyList<string> Crusts,
    IReadOnlyList<PizzaTopping> Toppings,
    decimal TaxRate,
    IReadOnlyList<PromotionDescription> Promotions)
{
    public static PizzaMenu Default { get; } = new(
        [new("Small", 5.00m), new("Medium", 7.00m), new("Large", 12.00m)],
        ["Classic", "Thin", "Gluten-free"],
        [
            new("pepperoni", "Pepperoni", "Basic", 0.08m),
            new("sausage", "Sausage", "Basic", 0.08m),
            new("extra-cheese", "Extra cheese", "Basic", 0.08m),
            new("onions", "Onions", "Basic", 0.08m),
            new("black-olives", "Black olives", "Basic", 0.08m),
            new("green-peppers", "Green peppers", "Basic", 0.08m),
            new("bacon", "Bacon", "Premium", 0.11m),
            new("chicken", "Chicken", "Premium", 0.11m),
            new("garlic", "Garlic", "Premium", 0.11m),
            new("artichoke-hearts", "Artichoke hearts", "Premium", 0.11m),
            new("sun-dried-tomatoes", "Sun-dried tomatoes", "Premium", 0.11m),
            new("pineapple", "Pineapple", "Premium", 0.11m)
        ],
        0.065m,
        [
            new("ILOVEPIZZA", "10% off eligible pre-tax subtotal", "Percentage", 0.10m, null),
            new("SAVE5", "$5 off when pre-discount subtotal is greater than $20", "FixedAmount", null, 5.00m, 20.00m)
        ]);
}

/// <param name="MinimumSubtotalExclusive">The pre-discount subtotal must be strictly greater than this for the promotion to apply.</param>
public sealed record PromotionDescription(string Code, string Description, string Type, decimal? Rate, decimal? Amount, decimal? MinimumSubtotalExclusive = null);
