using System.Text.Json;
using LongPizzaOrderBuilder.OrderService;
using LongPizzaOrderBuilder.Pricing;
using Xunit;

namespace LongPizzaOrderBuilder.UnitTests;

/// <summary>
/// Invariants about an order's life in the order service (docs/INVARIANTS.md: LIFE-001 to LIFE-005, ORD-003).
/// The expected rules are written out here independently of <see cref="OrderLifecycle"/>, so a mistake in the
/// production table cannot also hide in the test.
/// </summary>
public sealed class OrderLifecycleTests
{
    private static readonly OrderStatus[] All =
        [OrderStatus.NotStarted, OrderStatus.Started, OrderStatus.ReadyForPickup, OrderStatus.Delivered, OrderStatus.Cancelled];

    /// <summary>The only legal moves and who may make them, written out by hand.</summary>
    private static readonly HashSet<(OrderStatus From, OrderStatus To, OrderActor Actor)> Legal =
    [
        (OrderStatus.NotStarted, OrderStatus.Started, OrderActor.Restaurant),
        (OrderStatus.Started, OrderStatus.ReadyForPickup, OrderActor.Restaurant),
        (OrderStatus.ReadyForPickup, OrderStatus.Delivered, OrderActor.Restaurant),
        (OrderStatus.NotStarted, OrderStatus.Cancelled, OrderActor.Customer),
    ];

    private static OrderStore NewStore() => new(new PricingService(), new TickingClock());

    internal static OrderRequest Request(string name = "Sam", int quantity = 1, string size = "Medium", string? promo = null, TipRequest? tip = null) =>
        new(name, [new(size, "Classic", ["pepperoni"], quantity)], promo, tip);

    private static Order NewOrder(OrderStore store, string name = "Sam") => store.Create(Request(name), Guid.NewGuid().ToString()).Order;

    /// <summary>Brings a new order to <paramref name="target"/> using only legal moves.</summary>
    private static Order Reach(OrderStore store, OrderStatus target)
    {
        var order = NewOrder(store);
        (OrderStatus To, OrderActor Actor)[] path = target switch
        {
            OrderStatus.NotStarted => [],
            OrderStatus.Started => [(OrderStatus.Started, OrderActor.Restaurant)],
            OrderStatus.ReadyForPickup => [(OrderStatus.Started, OrderActor.Restaurant), (OrderStatus.ReadyForPickup, OrderActor.Restaurant)],
            OrderStatus.Delivered => [(OrderStatus.Started, OrderActor.Restaurant), (OrderStatus.ReadyForPickup, OrderActor.Restaurant), (OrderStatus.Delivered, OrderActor.Restaurant)],
            _ => [(OrderStatus.Cancelled, OrderActor.Customer)],
        };
        foreach (var (to, actor) in path) Assert.Equal(ChangeOutcome.Applied, store.Transition(order.Id, to, actor).Outcome);
        return store.Find(order.Id)!;
    }

    [Fact, Trait("Invariant", "LIFE-001")]
    public void Only_the_documented_moves_are_allowed_and_a_refused_move_changes_nothing()
    {
        foreach (var from in All)
            foreach (var to in All)
                foreach (var actor in new[] { OrderActor.Customer, OrderActor.Restaurant })
                {
                    var store = NewStore();
                    var before = Reach(store, from);
                    var result = store.Transition(before.Id, to, actor);
                    var after = store.Find(before.Id)!;

                    var expected = from == to ? ChangeOutcome.NoChange : Legal.Contains((from, to, actor)) ? ChangeOutcome.Applied : ChangeOutcome.Refused;
                    Assert.Equal(expected, result.Outcome);
                    Assert.Equal(expected == ChangeOutcome.Applied ? to : from, after.Status);
                    if (expected != ChangeOutcome.Applied)
                    {
                        Assert.Equal(before.Version, after.Version);
                        Assert.Equal(before.History.Count, after.History.Count);
                        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
                    }
                }
    }

    [Fact, Trait("Invariant", "LIFE-001")]
    public void Delivered_and_cancelled_orders_can_never_change_again()
    {
        foreach (var final in new[] { OrderStatus.Delivered, OrderStatus.Cancelled })
            foreach (var to in All.Where(status => status != final))
                foreach (var actor in new[] { OrderActor.Customer, OrderActor.Restaurant })
                {
                    var store = NewStore();
                    var order = Reach(store, final);
                    Assert.Equal(ChangeOutcome.Refused, store.Transition(order.Id, to, actor).Outcome);
                    Assert.Equal(final, store.Find(order.Id)!.Status);
                }
    }

    [Fact, Trait("Invariant", "LIFE-001")]
    public void A_move_on_an_unknown_order_is_not_found()
    {
        Assert.Equal(ChangeOutcome.NotFound, NewStore().Transition("nope", OrderStatus.Started, OrderActor.Restaurant).Outcome);
    }

    [Fact, Trait("Invariant", "LIFE-002")]
    public void History_always_tells_the_true_story_of_the_current_order()
    {
        var random = new Random(20261029);
        var store = NewStore();
        for (var n = 0; n < 400; n++)
        {
            var order = NewOrder(store, $"Customer {n}");
            for (var attempt = 0; attempt < random.Next(1, 14); attempt++)
            {
                var current = store.Find(order.Id)!;
                if (random.Next(3) == 0)
                    store.Update(order.Id, Request($"Customer {n}", random.Next(1, 11)), current.Version);
                else
                    store.Transition(order.Id, All[random.Next(All.Length)], random.Next(2) == 0 ? OrderActor.Customer : OrderActor.Restaurant);
            }
            AssertHistoryIsFaithful(store.Find(order.Id)!);
        }
    }

    internal static void AssertHistoryIsFaithful(Order order)
    {
        var history = order.History;
        Assert.Equal(history.Count, order.Version);                          // the version counts the recorded events
        Assert.Equal(new OrderEvent("Placed", OrderStatus.NotStarted, order.CreatedAt, OrderActor.Customer), history[0]);
        Assert.Equal(history[^1].Status, order.Status);                      // the status is where the story ended
        Assert.Equal(history[^1].At, order.UpdatedAt);
        for (var i = 1; i < history.Count; i++)
        {
            var (previous, change) = (history[i - 1], history[i]);
            Assert.True(change.At > previous.At);                            // in order, with distinct times
            if (change.Event == "Updated")
            {
                Assert.Equal(OrderStatus.NotStarted, previous.Status);      // an update only ever happens before the restaurant starts
                Assert.Equal(OrderStatus.NotStarted, change.Status);
                Assert.Equal(OrderActor.Customer, change.Actor);
            }
            else
            {
                Assert.Equal(change.Status.ToString(), change.Event);
                Assert.True(Legal.Contains((previous.Status, change.Status, change.Actor)));   // every recorded move was legal
            }
        }
    }

    [Fact, Trait("Invariant", "LIFE-003")]
    public void While_the_restaurant_is_half_way_through_starting_an_order_a_cancel_must_wait_and_is_then_refused()
    {
        // The restaurant has checked the order (NotStarted -> Started is fine) but has not yet written the change. Freeze it there,
        // then send the customer's cancel. A correct store makes the cancel wait; it then sees Started and is refused.
        var clock = new PausingClock();
        var store = new OrderStore(new PricingService(), clock);
        var order = NewOrder(store);

        clock.PauseNextCall();
        var restaurant = DedicatedThread.Run(() => store.Transition(order.Id, OrderStatus.Started, OrderActor.Restaurant));
        Assert.True(clock.Paused.Wait(TimeSpan.FromSeconds(5)), "The restaurant never reached the pause point.");
        var cancel = DedicatedThread.Run(() => store.Transition(order.Id, OrderStatus.Cancelled, OrderActor.Customer));
        var cancelGotInEarly = cancel.Wait(TimeSpan.FromMilliseconds(300));
        clock.Resume.Set();
        Task.WaitAll(restaurant, cancel);

        Assert.False(cancelGotInEarly, "The cancel was allowed in while the restaurant's change was half done.");
        Assert.Equal(ChangeOutcome.Applied, restaurant.Result.Outcome);
        Assert.Equal(ChangeOutcome.Refused, cancel.Result.Outcome);
        Assert.Contains("started", cancel.Result.Message);
        Assert.Equal(OrderStatus.Started, cancel.Result.Order!.Status);   // the customer is told the truth
        var final = store.Find(order.Id)!;
        Assert.Equal(OrderStatus.Started, final.Status);
        Assert.Equal(2, final.History.Count);
    }

    [Fact, Trait("Invariant", "LIFE-003")]
    public void While_a_customer_cancel_is_half_way_through_the_restaurant_must_wait_and_is_then_refused()
    {
        // The same race the other way round: the cancel got there first.
        var clock = new PausingClock();
        var store = new OrderStore(new PricingService(), clock);
        var order = NewOrder(store);

        clock.PauseNextCall();
        var cancel = DedicatedThread.Run(() => store.Transition(order.Id, OrderStatus.Cancelled, OrderActor.Customer));
        Assert.True(clock.Paused.Wait(TimeSpan.FromSeconds(5)), "The cancel never reached the pause point.");
        var restaurant = DedicatedThread.Run(() => store.Transition(order.Id, OrderStatus.Started, OrderActor.Restaurant));
        var restaurantGotInEarly = restaurant.Wait(TimeSpan.FromMilliseconds(300));
        clock.Resume.Set();
        Task.WaitAll(cancel, restaurant);

        Assert.False(restaurantGotInEarly, "The restaurant was allowed in while the cancel was half done.");
        Assert.Equal(ChangeOutcome.Applied, cancel.Result.Outcome);
        Assert.Equal(ChangeOutcome.Refused, restaurant.Result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, restaurant.Result.Order!.Status);
        Assert.Equal(OrderStatus.Cancelled, store.Find(order.Id)!.Status);
    }

    [Fact, Trait("Invariant", "LIFE-003")]
    public void Many_threads_changing_one_order_never_corrupt_it()
    {
        var store = NewStore();
        var order = NewOrder(store);
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, worker =>
        {
            var random = new Random(worker);
            for (var i = 0; i < 300; i++)
            {
                if (random.Next(3) == 0) store.Update(order.Id, Request("Sam", random.Next(1, 11)), store.Find(order.Id)!.Version);
                else store.Transition(order.Id, All[random.Next(All.Length)], random.Next(2) == 0 ? OrderActor.Customer : OrderActor.Restaurant);
            }
        });
        AssertHistoryIsFaithful(store.Find(order.Id)!);
    }

    [Fact, Trait("Invariant", "LIFE-004")]
    public void The_restaurant_can_never_cancel_and_the_service_says_whether_the_customer_can_still_change_the_order()
    {
        foreach (var from in All)
        {
            var store = NewStore();
            var order = Reach(store, from);
            var byRestaurant = store.Transition(order.Id, OrderStatus.Cancelled, OrderActor.Restaurant);

            // Refused, except that asking for the status an order already has is a harmless no-op.
            Assert.Equal(from == OrderStatus.Cancelled ? ChangeOutcome.NoChange : ChangeOutcome.Refused, byRestaurant.Outcome);
            Assert.Equal(from == OrderStatus.Cancelled, store.Find(order.Id)!.Status == OrderStatus.Cancelled);
            Assert.Equal(from == OrderStatus.NotStarted, order.CanCustomerChange);
        }
    }

    [Fact, Trait("Invariant", "LIFE-004")]
    public void A_late_cancel_is_refused_with_the_reason_and_the_current_status()
    {
        var store = NewStore();
        var order = Reach(store, OrderStatus.Started);
        var refused = store.Transition(order.Id, OrderStatus.Cancelled, OrderActor.Customer);
        Assert.Equal(ChangeOutcome.Refused, refused.Outcome);
        Assert.Contains("started", refused.Message);
        Assert.Equal(OrderStatus.Started, refused.Order!.Status);
        Assert.False(refused.Order.CanCustomerChange);
    }

    [Fact, Trait("Invariant", "LIFE-005")]
    public void Repeating_a_cancel_or_a_restaurant_move_is_harmless_and_records_nothing_new()
    {
        var store = NewStore();
        var order = NewOrder(store);
        Assert.Equal(ChangeOutcome.Applied, store.Transition(order.Id, OrderStatus.Cancelled, OrderActor.Customer).Outcome);
        var once = store.Find(order.Id)!;
        Assert.Equal(ChangeOutcome.NoChange, store.Transition(order.Id, OrderStatus.Cancelled, OrderActor.Customer).Outcome);
        var twice = store.Find(order.Id)!;
        Assert.Equal(once.Version, twice.Version);
        Assert.Equal(once.UpdatedAt, twice.UpdatedAt);

        var kitchen = Reach(store, OrderStatus.Started);
        Assert.Equal(ChangeOutcome.NoChange, store.Transition(kitchen.Id, OrderStatus.Started, OrderActor.Restaurant).Outcome);
        Assert.Equal(2, store.Find(kitchen.Id)!.History.Count);
    }

    [Fact, Trait("Invariant", "ORD-003")]
    public void Status_changes_refusals_and_repeats_never_alter_the_items_or_price()
    {
        var store = NewStore();
        var order = store.Create(Request(quantity: 2, promo: "ILOVEPIZZA", tip: new TipRequest("Percent", 15m)), "k").Order;
        string Content(Order o) => JsonSerializer.Serialize(new { o.CustomerName, o.Items, o.PromoCode, o.Tip, o.Pricing, o.CreatedAt });
        var before = Content(order);

        store.Transition(order.Id, OrderStatus.Started, OrderActor.Restaurant);
        store.Transition(order.Id, OrderStatus.Started, OrderActor.Restaurant);      // repeat
        store.Transition(order.Id, OrderStatus.Cancelled, OrderActor.Customer);      // refused
        store.Update(order.Id, Request(quantity: 9), store.Find(order.Id)!.Version); // refused
        store.Transition(order.Id, OrderStatus.ReadyForPickup, OrderActor.Restaurant);
        store.Transition(order.Id, OrderStatus.Delivered, OrderActor.Restaurant);

        Assert.Equal(before, Content(store.Find(order.Id)!));
    }
}
