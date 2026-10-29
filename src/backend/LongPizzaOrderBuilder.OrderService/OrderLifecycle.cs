namespace LongPizzaOrderBuilder.OrderService;

/// <summary>Where an order is. Serialized as its name, for example "ReadyForPickup".</summary>
public enum OrderStatus
{
    /// <summary>Accepted. The restaurant has not started. The customer can still cancel it.</summary>
    NotStarted,
    /// <summary>The restaurant is making it.</summary>
    Started,
    ReadyForPickup,
    /// <summary>Final.</summary>
    Delivered,
    /// <summary>The customer cancelled it before it started. Final. (The brief lists four statuses; a cancelled order needs an honest status of its own.)</summary>
    Cancelled,
}

/// <summary>
/// Who is asking. There is no authentication in this project, so the actor is decided by which door the request came through
/// (the customer API or the restaurant API), never by anything the caller claims.
/// </summary>
public enum OrderActor
{
    Customer,
    Restaurant,
}

/// <summary>One entry in an order's history. <see cref="Event"/> is "Placed", or the name of the status the order moved to.</summary>
public sealed record OrderEvent(string Event, OrderStatus Status, DateTimeOffset At, OrderActor Actor);

/// <summary>
/// The rules for moving between statuses, in one place.
/// <code>
///   NotStarted -> Started -> ReadyForPickup -> Delivered      (the restaurant moves these, one step at a time)
///   NotStarted -> Cancelled                                    (only the customer, only before the restaurant starts)
///   Delivered, Cancelled                                       (final)
/// </code>
/// The customer can cancel only while the order is NotStarted. (Changing a placed order is not built; see docs/FUTURE-WORK.md.)
/// </summary>
public static class OrderLifecycle
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Allowed = new Dictionary<OrderStatus, OrderStatus[]>
    {
        [OrderStatus.NotStarted] = [OrderStatus.Started, OrderStatus.Cancelled],
        [OrderStatus.Started] = [OrderStatus.ReadyForPickup],
        [OrderStatus.ReadyForPickup] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static bool IsFinal(OrderStatus status) => Allowed[status].Length == 0;

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed[from].Contains(to);

    /// <summary>The next step the restaurant takes, or null when the order is final.</summary>
    public static OrderStatus? NextStatus(OrderStatus status) =>
        status == OrderStatus.Cancelled ? null : Allowed[status].Where(next => next != OrderStatus.Cancelled).Cast<OrderStatus?>().FirstOrDefault();

    /// <summary>Whether <paramref name="actor"/> may make a move that is already legal: the customer only cancels, the restaurant never does.</summary>
    public static bool IsActorAllowed(OrderActor actor, OrderStatus from, OrderStatus to) =>
        actor == OrderActor.Customer ? to == OrderStatus.Cancelled : to != OrderStatus.Cancelled;

    /// <summary>Whether the customer can still cancel. The service decides this, so the browser never has to copy the rule.</summary>
    public static bool CanCustomerCancel(OrderStatus status) => status == OrderStatus.NotStarted;
}
