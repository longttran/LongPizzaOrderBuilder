using System.Collections.Concurrent;

namespace LongPizzaOrderBuilder.Api;

public sealed class PizzaOrderStore
{
    private readonly ConcurrentDictionary<string, PizzaOrder> _orders = new(StringComparer.Ordinal);
    private readonly PricingService _pricing;

    public PizzaOrderStore(PricingService pricing) => _pricing = pricing;

    public IReadOnlyList<PizzaOrder> All() => _orders.Values
        .OrderByDescending(order => order.CreatedAt)
        .ToArray();

    public PizzaOrder? Find(string id) => _orders.GetValueOrDefault(id);

    public PizzaOrder Add(CreatePizzaOrderRequest request)
    {
        var items = request.Items is { Count: > 0 }
            ? request.Items
            : request.Size is not null
                ? [new PizzaOrderItemRequest(request.Size, request.Crust ?? "Classic", request.Toppings ?? [], request.Quantity ?? 1)]
                : [];

        var preview = _pricing.Calculate(new PricingPreviewRequest(items, request.PromoCode, request.Tip));
        if (!preview.IsValid)
        {
            IReadOnlyList<string> errors = preview.Errors.Count > 0
                ? preview.Errors
                : new[] { "Pricing could not be calculated for this order." };
            throw new PricingValidationException(errors);
        }

        var order = new PizzaOrder(
            Guid.NewGuid().ToString("N"),
            request.CustomerName.Trim(),
            items.Select(item => item with { Toppings = item.Toppings.ToArray().ToList() }).ToArray(),
            preview.Breakdown!,
            "Submitted",
            DateTimeOffset.UtcNow);
        if (!_orders.TryAdd(order.Id, order))
            throw new InvalidOperationException("A unique order ID could not be generated.");
        return order;
    }
}

public sealed record CreatePizzaOrderRequest(
    string CustomerName,
    string? Size = null,
    string? Crust = "Classic",
    List<string>? Toppings = null,
    int? Quantity = 1,
    List<PizzaOrderItemRequest>? Items = null,
    string? PromoCode = null,
    TipRequest? Tip = null);

public sealed record PizzaOrder(
    string Id,
    string CustomerName,
    IReadOnlyList<PizzaOrderItemRequest> Items,
    PricingBreakdown Pricing,
    string Status,
    DateTimeOffset CreatedAt)
{
    // Kept for compatibility with the starter UI while it transitions to the breakdown model.
    public decimal Total => Pricing.Total;
    public string? Size => Items.Count == 1 ? Items[0].Size : null;
    public string? Crust => Items.Count == 1 ? Items[0].Crust : null;
    public IReadOnlyList<string> Toppings => Items.Count == 1 ? Items[0].Toppings : [];
    public int Quantity => Items.Sum(item => item.Quantity);
}

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
            new("SAVE5", "$5 off when pre-discount subtotal is greater than $20", "FixedAmount", null, 5.00m)
        ]);
}

public sealed record PromotionDescription(string Code, string Description, string Type, decimal? Rate, decimal? Amount);
