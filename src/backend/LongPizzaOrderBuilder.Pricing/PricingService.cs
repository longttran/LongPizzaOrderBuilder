using System.Globalization;

namespace LongPizzaOrderBuilder.Pricing;

/// <summary>
/// The single source of truth for price. Preview and order submission both call <see cref="Calculate"/>.
/// All prices, rates, the tax rate and promotion rules come from the injected <see cref="PizzaMenu"/>, so
/// changing the menu data changes prices without changing this code (invariant PRC-015).
/// </summary>
public sealed class PricingService(PizzaMenu menu)
{
    private const int MaxQuantityPerLine = 10;
    private const int MaxLinesPerOrder = 50;
    private static readonly MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    /// <summary>Uses the default menu. Kept so existing callers and tests keep working.</summary>
    public PricingService() : this(PizzaMenu.Default) { }

    public PricingPreviewResponse Calculate(PricingPreviewRequest request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            var invalidOrderPromotion = string.IsNullOrWhiteSpace(request.PromoCode)
                ? new PromotionEvaluation(null, false, true, "No promotion selected.", 0m)
                : new PromotionEvaluation(request.PromoCode.Trim().ToUpperInvariant(), false, false,
                    "Promotion was not evaluated because the order contains invalid selections.", 0m);
            return new PricingPreviewResponse(null, invalidOrderPromotion, errors);
        }

        var lines = new List<PricedOrderLine>();
        foreach (var item in request.Items)
        {
            var size = menu.Sizes.Single(s => s.Name.Equals(item.Size, StringComparison.OrdinalIgnoreCase));
            var toppingLines = item.Toppings.Select(id =>
            {
                var topping = menu.Toppings.Single(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                return new PricedTopping(topping.Id, topping.Name, Money(size.Price * topping.Rate), topping.Rate);
            }).ToList();
            var toppingsPerPizza = toppingLines.Sum(t => t.UnitPrice);
            var unitPrice = Money(size.Price + toppingsPerPizza);
            var lineTotal = Money(unitPrice * item.Quantity);
            lines.Add(new PricedOrderLine(item.Size, item.Crust, item.Toppings.ToArray(), item.Quantity,
                size.Price, toppingLines, unitPrice, lineTotal));
        }

        var subtotal = Money(lines.Sum(line => line.LineTotal));
        var promotion = EvaluatePromotion(request.PromoCode, subtotal);
        var discount = promotion.Applied ? Money(promotion.DiscountAmount) : 0m;
        var discountedSubtotal = Money(Math.Max(0m, subtotal - discount));
        var tax = Money(discountedSubtotal * menu.TaxRate);
        var tip = CalculateTip(request.Tip, discountedSubtotal + tax);
        var total = Money(discountedSubtotal + tax + tip);
        var breakdown = new PricingBreakdown(lines, subtotal, promotion, discount, discountedSubtotal,
            menu.TaxRate, tax, tip, total, "AwayFromZero to 2 decimal places");
        return new PricingPreviewResponse(breakdown, promotion, []);
    }

    private List<string> Validate(PricingPreviewRequest request)
    {
        var errors = new List<string>();
        if (request.Items is null || request.Items.Count == 0)
        {
            errors.Add("Add at least one pizza to calculate an order.");
            return errors;
        }
        if (request.Items.Count > MaxLinesPerOrder)
        {
            errors.Add($"An order can have at most {MaxLinesPerOrder} pizza lines.");
            return errors;
        }
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            var prefix = $"Item {i + 1}: ";
            if (item is null)
            {
                errors.Add(prefix + "must be a valid pizza configuration.");
                continue;
            }
            if (!menu.Sizes.Any(s => s.Name.Equals(item.Size, StringComparison.OrdinalIgnoreCase)))
                errors.Add(prefix + "choose a valid pizza size.");
            if (!menu.Crusts.Contains(item.Crust, StringComparer.OrdinalIgnoreCase))
                errors.Add(prefix + "choose a valid crust.");
            if (item.Quantity is < 1 or > MaxQuantityPerLine)
                errors.Add(prefix + $"quantity must be between 1 and {MaxQuantityPerLine}.");
            if (item.Toppings is null)
            {
                errors.Add(prefix + "toppings cannot be null.");
                continue;
            }
            if (item.Toppings.Distinct(StringComparer.OrdinalIgnoreCase).Count() != item.Toppings.Count)
                errors.Add(prefix + "each topping may only be selected once.");
            if (item.Toppings.Any(id => !menu.Toppings.Any(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
                errors.Add(prefix + "one or more toppings are invalid.");
        }
        if (request.Tip is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Tip.Type))
            {
                errors.Add("Tip type must be None, Percent, or Custom.");
            }
            else if (request.Tip.Type.Equals("Percent", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Tip.Percent is null or < 0m or > 100m)
                    errors.Add("Percentage tip must be between 0 and 100.");
            }
            else if (request.Tip.Type.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Tip.Amount is null or < 0m or > 10000m)
                    errors.Add("Custom tip must be between $0 and $10,000.");
            }
            else if (!request.Tip.Type.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Tip type must be None, Percent, or Custom.");
            }
        }
        return errors;
    }

    /// <summary>
    /// Evaluates at most one promotion code against rules read from the menu data. An unknown or ineligible
    /// code never reduces the total, and a discount can never exceed the subtotal (PRC-007, PRC-008).
    /// </summary>
    private PromotionEvaluation EvaluatePromotion(string? code, decimal subtotal)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new PromotionEvaluation(null, false, true, "No promotion selected.", 0m);
        var normalized = code.Trim().ToUpperInvariant();
        var promotion = menu.Promotions.FirstOrDefault(p => p.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (promotion is null)
            return new PromotionEvaluation(normalized, false, false, "This promotion code is not recognized.", 0m);

        if (promotion.MinimumSubtotalExclusive is { } minimum && subtotal <= minimum)
            return new PromotionEvaluation(normalized, false, false, string.Create(CultureInfo.InvariantCulture,
                $"{promotion.Code} requires a pre-discount subtotal greater than ${minimum:0.00}."), 0m);

        switch (promotion.Type)
        {
            case "Percentage":
                var percentOff = Math.Min(Money(subtotal * (promotion.Rate ?? 0m)), subtotal);
                return new PromotionEvaluation(normalized, true, true, string.Create(CultureInfo.InvariantCulture,
                    $"{(promotion.Rate ?? 0m) * 100:0.##}% discount applied to the pre-tax subtotal."), percentOff);
            case "FixedAmount":
                var amountOff = Math.Min(Money(promotion.Amount ?? 0m), subtotal);
                return new PromotionEvaluation(normalized, true, true, string.Create(CultureInfo.InvariantCulture,
                    $"${promotion.Amount ?? 0m:0.##} discount applied."), amountOff);
            default:
                return new PromotionEvaluation(normalized, false, false, "This promotion is not available.", 0m);
        }
    }

    private static decimal CalculateTip(TipRequest? tip, decimal postTaxAmount)
    {
        if (tip is null || tip.Type.Equals("None", StringComparison.OrdinalIgnoreCase)) return 0m;
        if (tip.Type.Equals("Percent", StringComparison.OrdinalIgnoreCase))
            return Money(postTaxAmount * (tip.Percent ?? 0m) / 100m);
        if (tip.Type.Equals("Custom", StringComparison.OrdinalIgnoreCase)) return Money(tip.Amount ?? 0m);
        return 0m;
    }

    private static decimal Money(decimal amount) => decimal.Round(amount, 2, Rounding);
}

public sealed record PizzaOrderItemRequest(string Size, string Crust, List<string> Toppings, int Quantity);
public sealed record TipRequest(string Type, decimal? Percent = null, decimal? Amount = null);
public sealed record PricingPreviewRequest(List<PizzaOrderItemRequest> Items, string? PromoCode = null, TipRequest? Tip = null);
public sealed record PricedTopping(string Id, string Name, decimal UnitPrice, decimal Rate);
public sealed record PricedOrderLine(string Size, string Crust, IReadOnlyList<string> Toppings, int Quantity,
    decimal BaseUnitPrice, IReadOnlyList<PricedTopping> PricedToppings, decimal UnitPrice, decimal LineTotal);
public sealed record PromotionEvaluation(string? Code, bool Applied, bool Eligible, string Message, decimal DiscountAmount);
public sealed record PricingBreakdown(IReadOnlyList<PricedOrderLine> Lines, decimal Subtotal,
    PromotionEvaluation Promotion, decimal Discount, decimal DiscountedSubtotal, decimal TaxRate,
    decimal Tax, decimal Tip, decimal Total, string RoundingPolicy);
public sealed record PricingPreviewResponse(PricingBreakdown? Breakdown, PromotionEvaluation Promotion, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0 && Breakdown is not null;
}
public sealed class PricingValidationException(IReadOnlyList<string> errors) : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
