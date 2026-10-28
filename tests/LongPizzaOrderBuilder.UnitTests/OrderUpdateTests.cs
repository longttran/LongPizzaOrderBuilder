using System.Text.Json;
using LongPizzaOrderBuilder.OrderService;
using LongPizzaOrderBuilder.Pricing;
using Xunit;

namespace LongPizzaOrderBuilder.UnitTests;

/// <summary>Updating a placed order (ORD-005, ORD-006) and the race with the restaurant starting it (LIFE-003).</summary>
public sealed class OrderUpdateTests
{
    private static OrderStore NewStore() => new(new PricingService());
    private static OrderRequest Request(int quantity = 1, string size = "Medium", string name = "Sam", string? promo = null, TipRequest? tip = null) =>
        OrderLifecycleTests.Request(name, quantity, size, promo, tip);
    private static Order Place(OrderStore store, OrderRequest? request = null) => store.Create(request ?? Request(), Guid.NewGuid().ToString()).Order;
    private static string Snapshot(Order o) => JsonSerializer.Serialize(o);

    [Fact, Trait("Invariant", "ORD-005")]
    public void An_update_while_NotStarted_changes_the_order_and_the_service_reprices_it()
    {
        var store = NewStore();
        var order = Place(store);
        var change = Request(quantity: 2, size: "Large", name: "Sam B", promo: "ILOVEPIZZA", tip: new TipRequest("Percent", 15m));

        var result = store.Update(order.Id, change, order.Version);

        Assert.Equal(ChangeOutcome.Applied, result.Outcome);
        var updated = result.Order!;
        var expected = new PricingService().Calculate(new PricingPreviewRequest(change.Items, change.PromoCode, change.Tip)).Breakdown!;
        Assert.Equal(expected.Total, updated.Total);                  // priced by the service, with the same code the builder previews with
        Assert.Equal(2, updated.Items[0].Quantity);
        Assert.Equal("Large", updated.Items[0].Size);
        Assert.Equal("Sam B", updated.CustomerName);
        Assert.Equal("ILOVEPIZZA", updated.PromoCode);
        Assert.Equal(15m, updated.Tip!.Percent);
        Assert.Equal(order.Id, updated.Id);
        Assert.Equal(order.CreatedAt, updated.CreatedAt);
        Assert.Equal(OrderStatus.NotStarted, updated.Status);
        Assert.Equal(2, updated.Version);
        Assert.Equal("Updated", updated.History[^1].Event);
        Assert.Equal(updated, store.Find(order.Id));
    }

    [Fact, Trait("Invariant", "ORD-005")]
    public void Once_the_restaurant_has_started_an_update_is_refused_and_nothing_changes()
    {
        foreach (var status in new[] { OrderStatus.Started, OrderStatus.ReadyForPickup, OrderStatus.Delivered, OrderStatus.Cancelled })
        {
            var store = NewStore();
            var order = Place(store);
            if (status == OrderStatus.Cancelled) store.Transition(order.Id, OrderStatus.Cancelled, OrderActor.Customer);
            else foreach (var step in new[] { OrderStatus.Started, OrderStatus.ReadyForPickup, OrderStatus.Delivered }.TakeWhile(s => s <= status))
                store.Transition(order.Id, step, OrderActor.Restaurant);
            var before = Snapshot(store.Find(order.Id)!);

            var result = store.Update(order.Id, Request(quantity: 5), store.Find(order.Id)!.Version);

            Assert.Equal(ChangeOutcome.Refused, result.Outcome);
            Assert.NotNull(result.Message);
            Assert.Equal(status, result.Order!.Status);               // the reply carries the order as it now is
            Assert.Equal(before, Snapshot(store.Find(order.Id)!));    // and nothing changed
        }
    }

    [Fact, Trait("Invariant", "ORD-005")]
    public void An_invalid_update_is_rejected_and_changes_nothing()
    {
        var store = NewStore();
        var order = Place(store);
        var before = Snapshot(order);
        Assert.Throws<PricingValidationException>(() => store.Update(order.Id, Request(quantity: 0), order.Version));
        Assert.Throws<PricingValidationException>(() => store.Update(order.Id, new OrderRequest("Sam", [], null, null), order.Version));
        Assert.Throws<PricingValidationException>(() => store.Update(order.Id, Request(size: "XL"), order.Version));
        Assert.Equal(before, Snapshot(store.Find(order.Id)!));
    }

    [Fact, Trait("Invariant", "ORD-005")]
    public void Updating_an_unknown_order_is_not_found()
    {
        Assert.Equal(ChangeOutcome.NotFound, NewStore().Update("nope", Request(), 1).Outcome);
    }

    [Fact, Trait("Invariant", "ORD-006")]
    public void Sending_the_same_update_again_changes_the_order_once()
    {
        var store = NewStore();
        var order = Place(store);
        var change = Request(quantity: 3);

        Assert.Equal(ChangeOutcome.Applied, store.Update(order.Id, change, 1).Outcome);
        var afterFirst = Snapshot(store.Find(order.Id)!);

        // The reply was lost, so the customer's phone sends exactly the same request again, still based on version 1.
        var retry = store.Update(order.Id, change, 1);
        Assert.Equal(ChangeOutcome.NoChange, retry.Outcome);
        Assert.Equal(2, retry.Order!.Version);
        Assert.Equal(afterFirst, Snapshot(store.Find(order.Id)!));    // no second change, no second history entry
    }

    [Fact, Trait("Invariant", "ORD-006")]
    public void A_delayed_older_update_can_never_overwrite_a_newer_one()
    {
        var store = NewStore();
        var order = Place(store);
        var first = Request(quantity: 2);
        var second = Request(quantity: 3);

        Assert.Equal(ChangeOutcome.Applied, store.Update(order.Id, first, 1).Outcome);    // v1 -> v2
        Assert.Equal(ChangeOutcome.Applied, store.Update(order.Id, second, 2).Outcome);   // v2 -> v3
        var current = Snapshot(store.Find(order.Id)!);

        // The first update, delayed on a bad connection, finally arrives after the second.
        var late = store.Update(order.Id, first, 1);
        Assert.Equal(ChangeOutcome.Stale, late.Outcome);
        Assert.Equal(3, late.Order!.Version);                          // the caller learns the current version
        Assert.Equal(3, late.Order.Items[0].Quantity);
        Assert.Equal(current, Snapshot(store.Find(order.Id)!));        // and the newer order is untouched

        // A retry of the second update that had already been applied is simply recognised.
        Assert.Equal(ChangeOutcome.NoChange, store.Update(order.Id, second, 2).Outcome);
        Assert.Equal(current, Snapshot(store.Find(order.Id)!));
    }

    [Fact, Trait("Invariant", "ORD-006")]
    public void Two_updates_based_on_the_same_version_cannot_both_be_applied_even_at_the_same_moment()
    {
        // The first update has checked the version and priced the order but has not saved it yet. Freeze it, then send a second one
        // that was based on the same version. A correct store makes it wait, and it is then refused as out of date.
        var clock = new PausingClock();
        var store = new OrderStore(new PricingService(), clock);
        var order = Place(store);

        clock.PauseNextCall();
        var first = DedicatedThread.Run(() => store.Update(order.Id, Request(quantity: 2), order.Version));
        Assert.True(clock.Paused.Wait(TimeSpan.FromSeconds(5)), "The first update never reached the pause point.");
        var second = DedicatedThread.Run(() => store.Update(order.Id, Request(quantity: 3), order.Version));
        var secondGotInEarly = second.Wait(TimeSpan.FromMilliseconds(300));
        clock.Resume.Set();
        Task.WaitAll(first, second);

        Assert.False(secondGotInEarly, "The second update was allowed in while the first was half done.");
        Assert.Equal(ChangeOutcome.Applied, first.Result.Outcome);
        Assert.Equal(ChangeOutcome.Stale, second.Result.Outcome);
        Assert.Equal(2, store.Find(order.Id)!.Items[0].Quantity);
        Assert.Equal(2, store.Find(order.Id)!.Version);
    }

    [Fact, Trait("Invariant", "LIFE-003")]
    public void While_the_restaurant_is_half_way_through_starting_an_order_an_update_must_wait_and_is_then_refused()
    {
        // The restaurant has looked at the order and is starting it. Freeze it mid-change, then send the customer's update.
        // The update must not slip in: the restaurant starts exactly the order it saw, and the customer is told it is too late.
        var clock = new PausingClock();
        var store = new OrderStore(new PricingService(), clock);
        var order = Place(store, Request(quantity: 1));
        var originalTotal = order.Total;

        clock.PauseNextCall();
        var restaurant = DedicatedThread.Run(() => store.Transition(order.Id, OrderStatus.Started, OrderActor.Restaurant));
        Assert.True(clock.Paused.Wait(TimeSpan.FromSeconds(5)), "The restaurant never reached the pause point.");
        var update = DedicatedThread.Run(() => store.Update(order.Id, Request(quantity: 9), order.Version));
        var updateGotInEarly = update.Wait(TimeSpan.FromMilliseconds(300));
        clock.Resume.Set();
        Task.WaitAll(restaurant, update);

        Assert.False(updateGotInEarly, "The update was allowed in while the restaurant's change was half done.");
        Assert.Equal(ChangeOutcome.Applied, restaurant.Result.Outcome);
        Assert.Equal(ChangeOutcome.Refused, update.Result.Outcome);
        Assert.Equal(OrderStatus.Started, update.Result.Order!.Status);
        var final = store.Find(order.Id)!;
        Assert.Equal(1, final.Items[0].Quantity);                      // the order the restaurant started is the order it saw
        Assert.Equal(originalTotal, final.Total);
        Assert.Equal(["Placed", "Started"], final.History.Select(h => h.Event));
    }

    [Fact, Trait("Invariant", "LIFE-003")]
    public void While_an_update_is_half_way_through_the_restaurant_must_wait_and_then_starts_the_updated_order()
    {
        // The other order of events: the update got in first. The restaurant's start waits, and then starts what the customer last saved.
        var clock = new PausingClock();
        var store = new OrderStore(new PricingService(), clock);
        var order = Place(store, Request(quantity: 1));

        clock.PauseNextCall();
        var update = DedicatedThread.Run(() => store.Update(order.Id, Request(quantity: 4), order.Version));
        Assert.True(clock.Paused.Wait(TimeSpan.FromSeconds(5)), "The update never reached the pause point.");
        var restaurant = DedicatedThread.Run(() => store.Transition(order.Id, OrderStatus.Started, OrderActor.Restaurant));
        var restaurantGotInEarly = restaurant.Wait(TimeSpan.FromMilliseconds(300));
        clock.Resume.Set();
        Task.WaitAll(update, restaurant);

        Assert.False(restaurantGotInEarly, "The restaurant was allowed in while the update was half done.");
        Assert.Equal(ChangeOutcome.Applied, update.Result.Outcome);
        Assert.Equal(ChangeOutcome.Applied, restaurant.Result.Outcome);
        var final = store.Find(order.Id)!;
        Assert.Equal(4, final.Items[0].Quantity);
        Assert.Equal(["Placed", "Updated", "Started"], final.History.Select(h => h.Event));
        OrderLifecycleTests.AssertHistoryIsFaithful(final);
    }
}
