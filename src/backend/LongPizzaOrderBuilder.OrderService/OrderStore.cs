using System.Text.Json;
using LongPizzaOrderBuilder.Pricing;

namespace LongPizzaOrderBuilder.OrderService;

/// <summary>What the customer asks for. There are no price fields: the service always prices the order itself.</summary>
public sealed record OrderRequest(string CustomerName, List<PizzaOrderItemRequest> Items, string? PromoCode = null, TipRequest? Tip = null);

/// <summary>
/// An immutable snapshot of an order. Every change produces a new snapshot, so a reader can never see a status that disagrees
/// with the history. The items and the accepted price never change after the order is placed.
/// </summary>
public sealed record Order(
    string Id,
    string CustomerName,
    IReadOnlyList<PizzaOrderItemRequest> Items,
    PricingBreakdown Pricing,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderEvent> History)
{
    public decimal Total => Pricing.Total;

    /// <summary>Whether the customer can still cancel this order. The service decides, so the browser never copies the rule.</summary>
    public bool CanCustomerCancel => OrderLifecycle.CanCustomerCancel(Status);
}

public enum ChangeOutcome
{
    /// <summary>The order changed.</summary>
    Applied,
    /// <summary>The order was already in the requested status, so nothing changed. Repeating a request is safe.</summary>
    NoChange,
    /// <summary>Not allowed now (wrong status, or not for this actor). Nothing changed.</summary>
    Refused,
    NotFound,
}

/// <param name="Order">The order as it is now (after the change, or unchanged if not applied). Null only when not found.</param>
public sealed record ChangeResult(ChangeOutcome Outcome, Order? Order, string? Message);

/// <param name="Replayed">True when this was a retry of an earlier request, so no new order was made.</param>
public sealed record CreateResult(Order Order, bool Replayed);

/// <summary>Raised when an Idempotency-Key is reused for a different request.</summary>
public sealed class IdempotencyConflictException()
    : Exception("This Idempotency-Key was already used for a different order request.");

/// <summary>
/// The in-memory order store. Every read and every change goes through one lock, so:
/// <list type="bullet">
/// <item>checking an order's status and applying a change happen as one step, which means two simultaneous requests (a customer
/// cancelling while the restaurant starts) are decided one after the other and exactly one wins;</item>
/// <item>checking an idempotency key and saving the order happen as one step, so duplicates and retries create exactly one order;</item>
/// <item>readers only see complete, immutable <see cref="Order"/> snapshots.</item>
/// </list>
/// One lock is enough for one restaurant in one process. Several service instances would need a database transaction and a unique
/// key instead (docs/decisions/ADR-0009). Everything here is lost when the process restarts.
/// </summary>
public sealed class OrderStore
{
    private const int MaxNameLength = 80;

    private readonly object _gate = new();
    private readonly Dictionary<string, Order> _orders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Fingerprint, string OrderId)> _keys = new(StringComparer.Ordinal);
    private readonly PricingService _pricing;
    private readonly TimeProvider _time;

    public OrderStore(PricingService pricing, TimeProvider? time = null)
    {
        _pricing = pricing;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>All orders, newest first. For the restaurant's side only.</summary>
    public IReadOnlyList<Order> All()
    {
        lock (_gate) return _orders.Values.OrderByDescending(order => order.CreatedAt).ToArray();
    }

    public Order? Find(string id)
    {
        lock (_gate) return _orders.GetValueOrDefault(id);
    }

    /// <summary>
    /// Validates, prices and saves a new order. Repeating the same request with the same key returns the original order instead of
    /// creating another. Only successful creations are remembered, so a request that failed validation can be corrected and sent again with the same key.
    /// </summary>
    /// <exception cref="PricingValidationException">The request is invalid.</exception>
    /// <exception cref="IdempotencyConflictException">The key was used before for a different request.</exception>
    public CreateResult Create(OrderRequest request, string idempotencyKey)
    {
        var fingerprint = Fingerprint(request);
        lock (_gate)
        {
            if (_keys.TryGetValue(idempotencyKey, out var seen))
            {
                if (seen.Fingerprint != fingerprint) throw new IdempotencyConflictException();
                return new CreateResult(_orders[seen.OrderId], Replayed: true);
            }

            var name = ValidName(request);
            var pricing = Price(request);
            var now = _time.GetUtcNow();
            var order = new Order(
                Guid.NewGuid().ToString("N"), name, CopyItems(request), pricing,
                OrderStatus.NotStarted, now, now, [new OrderEvent("Placed", OrderStatus.NotStarted, now, OrderActor.Customer)]);
            _orders.Add(order.Id, order);
            _keys.Add(idempotencyKey, (fingerprint, order.Id));
            return new CreateResult(order, Replayed: false);
        }
    }

    /// <summary>
    /// Moves an order to another status: the customer cancelling, or the restaurant moving it along. The check against the current status
    /// and the change itself happen as one step. Asking for the status the order already has is a harmless no-op.
    /// </summary>
    public ChangeResult Transition(string id, OrderStatus to, OrderActor actor)
    {
        lock (_gate)
        {
            if (!_orders.TryGetValue(id, out var order))
                return new ChangeResult(ChangeOutcome.NotFound, null, "Order not found.");

            var from = order.Status;
            if (from == to)
                return new ChangeResult(ChangeOutcome.NoChange, order, $"The order is already {to}.");

            if (!OrderLifecycle.CanTransition(from, to) || !OrderLifecycle.IsActorAllowed(actor, from, to))
                return new ChangeResult(ChangeOutcome.Refused, order, RefusalMessage(from, to, actor));

            var now = _time.GetUtcNow();
            var changed = order with
            {
                Status = to,
                UpdatedAt = now,
                History = [.. order.History, new OrderEvent(to.ToString(), to, now, actor)],
            };
            _orders[id] = changed;
            return new ChangeResult(ChangeOutcome.Applied, changed, null);
        }
    }

    private static string RefusalMessage(OrderStatus from, OrderStatus to, OrderActor actor)
    {
        if (actor == OrderActor.Customer && to == OrderStatus.Cancelled && from != OrderStatus.Cancelled)
            return "The restaurant has already started this order, so it can no longer be cancelled.";
        if (actor == OrderActor.Customer) return "Customers can only cancel an order. The restaurant moves it along.";
        if (to == OrderStatus.Cancelled) return "Only the customer can cancel an order.";
        return OrderLifecycle.IsFinal(from) ? $"The order is {from} and can no longer change." : $"An order that is {from} cannot become {to}.";
    }

    private static string ValidName(OrderRequest request)
    {
        var name = request.CustomerName?.Trim() ?? "";
        if (name.Length == 0) throw new PricingValidationException(["Customer name is required."]);
        if (name.Length > MaxNameLength) throw new PricingValidationException([$"Customer name must be {MaxNameLength} characters or fewer."]);
        return name;
    }

    /// <summary>Prices with the same code the order builder previews with. Throws if the order is invalid.</summary>
    private PricingBreakdown Price(OrderRequest request)
    {
        var preview = _pricing.Calculate(new PricingPreviewRequest(request.Items ?? [], request.PromoCode, request.Tip));
        if (preview.IsValid) return preview.Breakdown!;
        throw new PricingValidationException(preview.Errors.Count > 0 ? preview.Errors : ["Pricing could not be calculated for this order."]);
    }

    private static List<PizzaOrderItemRequest> CopyItems(OrderRequest request) =>
        request.Items.Select(item => item with { Toppings = [.. item.Toppings] }).ToList();

    /// <summary>A canonical text form of what the customer asked for, used to tell a genuine retry from a reused key.</summary>
    private static string Fingerprint(OrderRequest request) =>
        JsonSerializer.Serialize(new
        {
            name = request.CustomerName?.Trim(),
            items = (request.Items ?? []).Select(i => i is null ? null : new { i.Size, i.Crust, Toppings = i.Toppings ?? [], i.Quantity }),
            promo = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim().ToUpperInvariant(),
            tip = request.Tip is null || string.Equals(request.Tip.Type, "None", StringComparison.OrdinalIgnoreCase)
                ? null
                : new { Type = request.Tip.Type?.ToUpperInvariant(), request.Tip.Percent, request.Tip.Amount },
        });
}
