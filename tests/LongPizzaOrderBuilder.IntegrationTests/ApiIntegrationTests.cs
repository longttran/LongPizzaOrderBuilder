using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongPizzaOrderBuilder.IntegrationTests;

public sealed class ApiIntegrationTests : IClassFixture<WebApplicationFactory<global::Program>>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<global::Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Menu_returns_agreed_prices_and_topping_rates()
    {
        var response = await _client.GetFromJsonAsync<MenuResponse>("/api/menu");

        Assert.NotNull(response);
        Assert.Equal(new[] { 5m, 7m, 12m }, response.Sizes.Select(size => size.Price));
        Assert.Equal(0.065m, response.TaxRate);
        Assert.Equal(0.08m, response.Toppings.Single(topping => topping.Id == "pepperoni").Rate);
        Assert.Equal(0.11m, response.Toppings.Single(topping => topping.Id == "bacon").Rate);
    }

    [Fact]
    public async Task Preview_returns_server_calculated_discount_tax_and_total()
    {
        var response = await _client.PostAsJsonAsync("/api/pricing/preview", new
        {
            items = new[] { new { size = "Small", crust = "Classic", toppings = Array.Empty<string>(), quantity = 1 } },
            promoCode = "ILOVEPIZZA",
            tip = new { type = "None" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PreviewResponse>();
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal(5m, result.Breakdown!.Subtotal);
        Assert.Equal(0.5m, result.Breakdown.Discount);
        Assert.Equal(0.29m, result.Breakdown.Tax);
        Assert.Equal(4.79m, result.Breakdown.Total);
    }

    [Fact]
    public async Task Preview_rejects_invalid_size_with_bad_request()
    {
        var response = await _client.PostAsJsonAsync("/api/pricing/preview", new
        {
            items = new[] { new { size = "Giant", crust = "Classic", toppings = Array.Empty<string>(), quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PreviewResponse>();
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("valid pizza size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Order_submission_returns_accepted_server_total_and_order_can_be_retrieved()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerName = "Integration Test",
            size = "Small",
            crust = "Classic",
            toppings = new[] { "pepperoni" },
            quantity = 1,
            promoCode = (string?)null,
            tip = new { type = "None" }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.Equal("Integration Test", order.CustomerName);
        Assert.Equal(5.75m, order.Total);
        Assert.Equal("Submitted", order.Status);
        Assert.NotNull(response.Headers.Location);

        var getResponse = await _client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var retrieved = await getResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(retrieved);
        Assert.Equal(order.Id, retrieved.Id);
    }

    [Fact]
    public async Task Order_submission_rejects_blank_customer_name_and_empty_order()
    {
        var blankName = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerName = "  ",
            size = "Small",
            crust = "Classic",
            toppings = Array.Empty<string>(),
            quantity = 1
        });
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);

        var emptyOrder = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerName = "Test",
            items = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyOrder.StatusCode);
    }

    public sealed record MenuResponse(IReadOnlyList<SizeResponse> Sizes, IReadOnlyList<ToppingResponse> Toppings, decimal TaxRate);
    public sealed record SizeResponse(string Name, decimal Price);
    public sealed record ToppingResponse(string Id, decimal Rate);
    public sealed record PreviewResponse(bool IsValid, PreviewBreakdown? Breakdown, IReadOnlyList<string> Errors);
    public sealed record PreviewBreakdown(decimal Subtotal, decimal Discount, decimal Tax, decimal Total);
    public sealed record OrderResponse(string Id, string CustomerName, decimal Total, string Status);
}
