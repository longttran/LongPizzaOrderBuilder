using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using LongPizzaOrderBuilder.OrderService;
using Xunit;

namespace LongPizzaOrderBuilder.IntegrationTests;

/// <summary>
/// The order service over real HTTP: what a customer's phone and the restaurant can do, what is refused, and what happens when
/// requests are repeated, delayed or sent at the same moment. See docs/LIFECYCLE.md.
/// </summary>
public sealed class OrderServiceApiTests : IClassFixture<WebApplicationFactory<OrderServiceHost>>
{
    private readonly HttpClient _client;

    public OrderServiceApiTests(WebApplicationFactory<OrderServiceHost> factory) => _client = factory.CreateClient();

    private static object Body(string name, int quantity = 1, string size = "Medium", object? extra = null) => new
    {
        customerName = name,
        items = new[] { new { size, crust = "Classic", toppings = new[] { "pepperoni" }, quantity } },
        tip = new { type = "None" },
    };

    private static HttpRequestMessage Post(string url, object? body, string? key = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (key is not null) message.Headers.Add("Idempotency-Key", key);
        return message;
    }

    private async Task<Order> Place(string name = "Service Test", int quantity = 1)
    {
        var response = await _client.SendAsync(Post("/api/orders", Body(name, quantity), Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Order>())!;
    }

    private Task<HttpResponseMessage> Cancel(string id) => _client.SendAsync(Post($"/api/orders/{id}/cancel", null));
    private Task<HttpResponseMessage> Restaurant(string id, string to) => _client.PostAsJsonAsync($"/api/restaurant/orders/{id}/transitions", new { to });
    private async Task<Order> Get(string id) => (await _client.GetFromJsonAsync<Order>($"/api/orders/{id}"))!;

    // ---- sending an order: ORD-001, ORD-002, ORD-004, API-001, API-004 ----

    [Fact, Trait("Invariant", "ORD-004")]
    public async Task Sending_an_order_returns_an_order_id_and_its_status()
    {
        var response = await _client.SendAsync(Post("/api/orders", Body("Id Test"), Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var order = await response.Content.ReadFromJsonAsync<Order>();
        Assert.False(string.IsNullOrEmpty(order!.Id));
        Assert.Equal("NotStarted", order.Status);
        Assert.True(order.CanCustomerCancel);
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public async Task Retrying_after_a_timeout_returns_the_original_order_and_creates_no_duplicate()
    {
        var name = $"Retry {Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString();

        var first = await _client.SendAsync(Post("/api/orders", Body(name), key));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadFromJsonAsync<Order>();
        // ...imagine the response never reached the customer. They send the same request with the same key:
        var retry = await _client.SendAsync(Post("/api/orders", Body(name), key));

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Contains("Idempotency-Replayed", retry.Headers.Select(header => header.Key));
        Assert.Equal(original!.Id, (await retry.Content.ReadFromJsonAsync<Order>())!.Id);

        var all = await _client.GetFromJsonAsync<List<Order>>("/api/restaurant/orders");
        Assert.Equal(1, all!.Count(o => o.CustomerName == name));   // still exactly one order
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public async Task Reusing_a_key_for_a_different_order_is_refused_with_422()
    {
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(Post("/api/orders", Body("Key reuse A"), key))).StatusCode);
        var other = await _client.SendAsync(Post("/api/orders", Body("Key reuse B"), key));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Contains("different", (await other.Content.ReadFromJsonAsync<Problem>())!.Errors[0]);
    }

    [Fact, Trait("Invariant", "ORD-004")]
    public async Task Simultaneous_duplicate_submissions_create_one_order()
    {
        var name = $"Parallel {Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString();
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _client.SendAsync(Post("/api/orders", Body(name), key))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(19, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var ids = new HashSet<string>();
        foreach (var response in responses) ids.Add((await response.Content.ReadFromJsonAsync<Order>())!.Id);
        Assert.Single(ids);
    }

    [Fact, Trait("Invariant", "API-004")]
    public async Task Sending_an_order_without_an_idempotency_key_or_with_a_malformed_one_is_refused()
    {
        var missing = await _client.SendAsync(Post("/api/orders", Body("No key")));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("Idempotency-Key", (await missing.Content.ReadFromJsonAsync<Problem>())!.Errors[0]);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(Post("/api/orders", Body("Bad key"), "has spaces and / slashes"))).StatusCode);
    }

    [Fact, Trait("Invariant", "ORD-001")]
    public async Task Invalid_orders_are_refused_and_not_stored()
    {
        var before = (await _client.GetFromJsonAsync<List<Order>>("/api/restaurant/orders"))!.Count;
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(Post("/api/orders", Body("  "), "k1"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(Post("/api/orders", Body(new string('x', 81)), "k2"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(Post("/api/orders", new { customerName = "Sam", items = Array.Empty<object>() }, "k3"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(Post("/api/orders", Body("Sam", size: "Giant"), "k4"))).StatusCode);
        Assert.Equal(before, (await _client.GetFromJsonAsync<List<Order>>("/api/restaurant/orders"))!.Count);
    }

    [Fact, Trait("Invariant", "API-001")]
    public async Task Prices_sent_by_the_client_are_ignored_and_the_service_prices_the_order()
    {
        var tampered = new
        {
            customerName = "Tamper",
            total = 0.01m, subtotal = 0.01m, tax = 0m, discount = 99m,
            items = new[] { new { size = "Medium", crust = "Classic", toppings = new[] { "pepperoni" }, quantity = 1, price = 0.01m, unitPrice = 0.01m, lineTotal = 0.01m } },
            tip = new { type = "None", amount = 0m },
        };
        var response = await _client.SendAsync(Post("/api/orders", tampered, Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(8.05m, (await response.Content.ReadFromJsonAsync<Order>())!.Total);   // Medium $7.00 + pepperoni $0.56, plus 6.5% tax
    }

    // ---- querying: LIFE-002 ----

    [Fact, Trait("Invariant", "LIFE-002")]
    public async Task Querying_an_order_returns_its_status_and_history()
    {
        var order = await Place();
        var response = await _client.GetAsync($"/api/orders/{order.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await response.Content.ReadFromJsonAsync<Order>();
        Assert.Equal("NotStarted", read!.Status);
        Assert.Equal(["Placed"], read.History.Select(h => h.Event));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/orders/does-not-exist")).StatusCode);
    }

    // ---- two separate doors: API-003 ----

    [Fact, Trait("Invariant", "API-003")]
    public async Task The_customer_door_cannot_list_orders_or_move_them_along()
    {
        var order = await Place();
        var list = await _client.GetAsync("/api/orders");
        Assert.True(list.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync($"/api/orders/{order.Id}/transitions", new { to = "Started" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync($"/api/orders/{order.Id}/advance", new { })).StatusCode);
        Assert.Equal("NotStarted", (await Get(order.Id)).Status);
    }

    [Fact, Trait("Invariant", "API-003")]
    public async Task The_restaurant_door_lists_orders_and_can_filter_by_status()
    {
        var order = await Place($"Listed {Guid.NewGuid():N}");
        var all = await _client.GetFromJsonAsync<List<Order>>("/api/restaurant/orders");
        Assert.Contains(all!, o => o.Id == order.Id);

        await Restaurant(order.Id, "Started");
        var notStarted = await _client.GetFromJsonAsync<List<Order>>("/api/restaurant/orders?status=NotStarted");
        var started = await _client.GetFromJsonAsync<List<Order>>("/api/restaurant/orders?status=Started");
        Assert.DoesNotContain(notStarted!, o => o.Id == order.Id);
        Assert.Contains(started!, o => o.Id == order.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/restaurant/orders?status=Burnt")).StatusCode);
    }

    // ---- the restaurant advances the order: LIFE-001 ----

    [Fact, Trait("Invariant", "LIFE-001")]
    public async Task The_restaurant_walks_an_order_through_every_status_and_it_then_stays_delivered()
    {
        var order = await Place();
        foreach (var status in new[] { "Started", "ReadyForPickup", "Delivered" })
        {
            var response = await Restaurant(order.Id, status);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(status, (await response.Content.ReadFromJsonAsync<Order>())!.Status);
        }
        var final = await Get(order.Id);
        Assert.Equal(["Placed", "Started", "ReadyForPickup", "Delivered"], final.History.Select(h => h.Event));
        Assert.Equal(4, final.History.Count);   // placed, started, ready for pickup, delivered
        Assert.False(final.CanCustomerCancel);
        Assert.Equal(HttpStatusCode.Conflict, (await Restaurant(order.Id, "Started")).StatusCode);      // no going back
        Assert.Equal(HttpStatusCode.Conflict, (await Cancel(order.Id)).StatusCode);                      // and no cancelling a delivered order
    }

    [Fact, Trait("Invariant", "LIFE-001")]
    public async Task The_restaurant_cannot_skip_a_step_or_use_an_unknown_status()
    {
        var order = await Place();
        Assert.Equal(HttpStatusCode.Conflict, (await Restaurant(order.Id, "ReadyForPickup")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Restaurant(order.Id, "Delivered")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Restaurant(order.Id, "Burnt")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/restaurant/orders/{order.Id}/transitions", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Restaurant("does-not-exist", "Started")).StatusCode);
        Assert.Equal("NotStarted", (await Get(order.Id)).Status);
    }

    [Fact, Trait("Invariant", "LIFE-004")]
    public async Task The_restaurant_cannot_cancel_an_order()
    {
        var order = await Place();
        Assert.Equal(HttpStatusCode.Conflict, (await Restaurant(order.Id, "Cancelled")).StatusCode);
        Assert.Equal("NotStarted", (await Get(order.Id)).Status);
    }

    // ---- cancelling: LIFE-004, LIFE-005 ----

    [Fact, Trait("Invariant", "LIFE-004")]
    public async Task A_customer_can_cancel_an_order_that_has_not_started_even_with_no_request_body()
    {
        var order = await Place();
        var response = await Cancel(order.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await response.Content.ReadFromJsonAsync<Order>();
        Assert.Equal("Cancelled", cancelled!.Status);
        Assert.False(cancelled.CanCustomerCancel);
        Assert.Equal(HttpStatusCode.NotFound, (await Cancel("does-not-exist")).StatusCode);
    }

    [Fact, Trait("Invariant", "LIFE-004")]
    public async Task Cancelling_after_the_restaurant_has_started_is_refused_with_the_reason_and_the_current_status()
    {
        var order = await Place();
        Assert.Equal(HttpStatusCode.OK, (await Restaurant(order.Id, "Started")).StatusCode);

        var refused = await Cancel(order.Id);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<Problem>();
        Assert.Contains("started", problem!.Errors[0]);
        Assert.Equal("Started", problem.Order!.Status);
        Assert.False(problem.Order.CanCustomerCancel);
        Assert.Equal("Started", (await Get(order.Id)).Status);
    }

    [Fact, Trait("Invariant", "LIFE-005")]
    public async Task Cancelling_twice_is_safe_and_records_one_cancellation()
    {
        var order = await Place();
        Assert.Equal(HttpStatusCode.OK, (await Cancel(order.Id)).StatusCode);
        var second = await Cancel(order.Id);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var final = await second.Content.ReadFromJsonAsync<Order>();
        Assert.Equal(["Placed", "Cancelled"], final!.History.Select(h => h.Event));
    }


    // ---- the customer changes their mind at the moment the restaurant starts: LIFE-003 ----

    [Fact, Trait("Invariant", "LIFE-003")]
    public async Task A_cancel_and_the_restaurant_starting_at_the_same_moment_have_exactly_one_winner()
    {
        for (var n = 0; n < 40; n++)
        {
            var order = await Place($"Race {n}");
            var responses = await Task.WhenAll(Cancel(order.Id), Restaurant(order.Id, "Started"));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));          // exactly one succeeded
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));    // the other was refused

            var final = await Get(order.Id);
            var cancelWon = responses[0].StatusCode == HttpStatusCode.OK;
            Assert.Equal(cancelWon ? "Cancelled" : "Started", final.Status);
            Assert.Equal(2, final.History.Count);

            // The refused caller is told the true state in the response body.
            var refused = await (cancelWon ? responses[1] : responses[0]).Content.ReadFromJsonAsync<Problem>();
            Assert.Equal(final.Status, refused!.Order!.Status);
        }
    }

    [Fact]
    public async Task Browsers_on_the_order_builders_address_may_call_the_service_with_the_retry_headers()
    {
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/orders");
        preflight.Headers.Add("Origin", "http://localhost:5173");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,idempotency-key");
        var response = await _client.SendAsync(preflight);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Idempotency-Key", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
    }

    public sealed record Order(string Id, string CustomerName, string Status, bool CanCustomerCancel, decimal Total, List<Item> Items, List<HistoryEvent> History);
    public sealed record Item(string Size, int Quantity);
    public sealed record HistoryEvent(string Event, string Status, string Actor);
    public sealed record Problem(List<string> Errors, Order? Order);
}
