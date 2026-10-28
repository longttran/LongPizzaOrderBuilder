using LongPizzaOrderBuilder.OrderService;
using LongPizzaOrderBuilder.Pricing;
using Xunit;

namespace LongPizzaOrderBuilder.UnitTests;

/// <summary>ORD-004: sending an order is safe to repeat. The HTTP behaviour (required header, 201/200/422) is covered by the integration tests.</summary>
public sealed class OrderIdempotencyTests
{
    private static OrderStore NewStore() => new(new PricingService());

    private static OrderRequest Request(string name = "Sam", int quantity = 1, string? promo = null, TipRequest? tip = null) =>
        OrderLifecycleTests.Request(name, quantity, "Medium", promo, tip);

    [Fact, Trait("Invariant", "ORD-004")]
    public void A_retry_with_the_same_key_returns_the_original_order_and_creates_no_second_one()
    {
        var store = NewStore();
        var first = store.Create(Request(), "key-1");
        var retry = store.Create(Request(), "key-1");

        Assert.False(first.Replayed);
        Assert.True(retry.Replayed);
        Assert.Equal(first.Order.Id, retry.Order.Id);
        Assert.Single(store.All());
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public void A_retry_after_the_order_has_moved_on_reports_its_current_status()
    {
        var store = NewStore();
        var first = store.Create(Request(quantity: 1), "key-2");
        store.Transition(first.Order.Id, OrderStatus.Started, OrderActor.Restaurant);

        var retry = store.Create(Request(quantity: 1), "key-2");   // the customer's original request, retried much later
        Assert.True(retry.Replayed);
        Assert.Equal(first.Order.Id, retry.Order.Id);
        Assert.Equal(OrderStatus.Started, retry.Order.Status);     // the truth, not the status at creation
        Assert.Single(store.All());
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public void Reusing_a_key_for_a_different_request_is_refused_and_creates_nothing()
    {
        var store = NewStore();
        store.Create(Request(quantity: 1), "key-3");

        Assert.Throws<IdempotencyConflictException>(() => store.Create(Request(quantity: 2), "key-3"));
        Assert.Throws<IdempotencyConflictException>(() => store.Create(Request(name: "Someone else"), "key-3"));
        Assert.Throws<IdempotencyConflictException>(() => store.Create(Request(tip: new TipRequest("Percent", 15m)), "key-3"));
        Assert.Single(store.All());
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public void Harmless_differences_in_spelling_still_count_as_the_same_request()
    {
        var store = NewStore();
        var first = store.Create(Request(name: "Sam", promo: "SAVE5", tip: new TipRequest("None")), "key-4");
        var retry = store.Create(Request(name: "  Sam  ", promo: " save5 ", tip: null), "key-4");
        Assert.Equal(first.Order.Id, retry.Order.Id);
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public void Different_keys_create_different_orders()
    {
        var store = NewStore();
        var ids = new[] { "key-a", "key-b", "key-c" }.Select(key => store.Create(Request(), key).Order.Id).Distinct().Count();
        Assert.Equal(3, ids);
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public void A_request_that_failed_validation_does_not_use_up_its_key()
    {
        var store = NewStore();
        Assert.Throws<PricingValidationException>(() => store.Create(Request(quantity: 0), "key-5"));
        Assert.Empty(store.All());

        var corrected = store.Create(Request(quantity: 1), "key-5");   // the customer corrected it and tried again
        Assert.False(corrected.Replayed);
        Assert.Single(store.All());
    }

    [Fact, Trait("Invariant", "ORD-001")]
    public void Malformed_input_is_a_validation_error_not_a_crash()
    {
        var store = NewStore();
        Assert.Throws<PricingValidationException>(() => store.Create(new OrderRequest("Sam", [null!], null, null), "key-6"));
        Assert.Throws<PricingValidationException>(() => store.Create(new OrderRequest("Sam", [new("Small", "Classic", null!, 1)], null, new TipRequest(null!)), "key-7"));
        Assert.Throws<PricingValidationException>(() => store.Create(new OrderRequest("Sam", [], null, null), "key-8"));
        Assert.Empty(store.All());
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public async Task A_retry_that_arrives_while_the_first_attempt_is_still_being_saved_waits_and_then_gets_the_same_order()
    {
        // The first attempt has checked the key (new) and priced the order but has not saved it yet. Freeze it there and send the retry.
        // A correct store makes the retry wait, then it finds the saved order. Without that, both would create an order.
        var clock = new PausingClock();
        var store = new OrderStore(new PricingService(), clock);

        clock.PauseNextCall();
        var first = DedicatedThread.Run(() => store.Create(Request(), "key-slow"));
        Assert.True(clock.Paused.Wait(TimeSpan.FromSeconds(5)), "The first attempt never reached the pause point.");
        var retry = DedicatedThread.Run(() => store.Create(Request(), "key-slow"));
        var retryGotInEarly = await DedicatedThread.FinishesWithin(retry, TimeSpan.FromMilliseconds(300));
        clock.Resume.Set();
        var firstResult = await first;
        var retryResult = await retry;

        Assert.False(retryGotInEarly, "The retry was allowed in while the first attempt was half done.");
        Assert.False(firstResult.Replayed);
        Assert.True(retryResult.Replayed);
        Assert.Equal(firstResult.Order.Id, retryResult.Order.Id);
        Assert.Single(store.All());
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public async Task Many_simultaneous_duplicate_submissions_create_exactly_one_order()
    {
        var store = NewStore();
        const int attempts = 64;
        using var gate = new Barrier(attempts);
        var results = Enumerable.Range(0, attempts)
            .Select(_ => Task.Factory.StartNew(() => { gate.SignalAndWait(); return store.Create(Request(), "key-race"); }, TaskCreationOptions.LongRunning))
            .ToArray();
        var created = await Task.WhenAll(results);

        Assert.Single(created.Select(r => r.Order.Id).Distinct());
        Assert.Single(created, r => !r.Replayed);                      // exactly one created it
        Assert.Single(store.All());
    }
}
