using LongPizzaOrderBuilder.OrderService;
using LongPizzaOrderBuilder.Pricing;
using Xunit;

namespace LongPizzaOrderBuilder.UnitTests;

/// <summary>LIFE-006: the timer that stands in for the restaurant moves orders safely.</summary>
public sealed class KitchenSimulatorTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);

    private static (OrderStore Store, ManualClock Clock) NewStore()
    {
        var clock = new ManualClock();
        return (new OrderStore(new PricingService(), clock), clock);
    }

    private static Order Place(OrderStore store, string key) => store.Create(OrderLifecycleTests.Request(), key).Order;

    [Fact, Trait("Invariant", "LIFE-006")]
    public void An_order_is_not_moved_until_it_has_waited_the_configured_time()
    {
        var (store, clock) = NewStore();
        var order = Place(store, "a");

        Assert.Null(KitchenSimulator.DueMove(order, clock.GetUtcNow(), Delay));
        clock.Advance(Delay - TimeSpan.FromSeconds(1));
        Assert.Null(KitchenSimulator.DueMove(order, clock.GetUtcNow(), Delay));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(OrderStatus.Started, KitchenSimulator.DueMove(order, clock.GetUtcNow(), Delay));
    }

    [Fact, Trait("Invariant", "LIFE-006")]
    public void The_simulation_is_off_when_the_delay_is_zero_or_negative()
    {
        var (store, clock) = NewStore();
        var order = Place(store, "a");
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Null(KitchenSimulator.DueMove(order, clock.GetUtcNow(), TimeSpan.Zero));
        Assert.Null(KitchenSimulator.DueMove(order, clock.GetUtcNow(), TimeSpan.FromSeconds(-5)));
        Assert.Equal(0, KitchenSimulator.RunOnce(store, clock, TimeSpan.Zero));
    }

    [Fact, Trait("Invariant", "LIFE-006")]
    public void Orders_advance_one_step_at_a_time_through_every_status_and_then_stop()
    {
        var (store, clock) = NewStore();
        var order = Place(store, "a");
        var seen = new List<OrderStatus> { order.Status };

        for (var tick = 0; tick < 12; tick++)
        {
            clock.Advance(Delay);
            KitchenSimulator.RunOnce(store, clock, Delay);
            KitchenSimulator.RunOnce(store, clock, Delay);   // a second run in the same instant must not move it again
            var status = store.Find(order.Id)!.Status;
            if (status != seen[^1]) seen.Add(status);
        }

        Assert.Equal([OrderStatus.NotStarted, OrderStatus.Started, OrderStatus.ReadyForPickup, OrderStatus.Delivered], seen);
        OrderLifecycleTests.AssertHistoryIsFaithful(store.Find(order.Id)!);
    }

    [Fact, Trait("Invariant", "LIFE-006")]
    public void A_cancelled_order_is_never_moved_and_a_cancel_wins_if_it_comes_before_the_timer()
    {
        var (store, clock) = NewStore();
        var order = Place(store, "a");
        store.Transition(order.Id, OrderStatus.Cancelled, OrderActor.Customer);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, KitchenSimulator.RunOnce(store, clock, Delay));
        Assert.Equal(OrderStatus.Cancelled, store.Find(order.Id)!.Status);
    }

    [Fact, Trait("Invariant", "LIFE-006")]
    public void The_timer_moves_every_due_order_and_leaves_the_others()
    {
        var (store, clock) = NewStore();
        var early = Place(store, "early");
        clock.Advance(TimeSpan.FromSeconds(20));
        var late = Place(store, "late");
        clock.Advance(TimeSpan.FromSeconds(15));        // early has now waited 35 s, late only 15 s

        Assert.Equal(1, KitchenSimulator.RunOnce(store, clock, Delay));
        Assert.Equal(OrderStatus.Started, store.Find(early.Id)!.Status);
        Assert.Equal(OrderStatus.NotStarted, store.Find(late.Id)!.Status);
    }
}
