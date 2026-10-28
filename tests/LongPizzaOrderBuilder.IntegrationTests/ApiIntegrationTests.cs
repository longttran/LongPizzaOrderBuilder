using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongPizzaOrderBuilder.IntegrationTests;

/// <summary>The order builder's own API: the menu and the price preview. Orders are handled by the separate order service.</summary>
public sealed class ApiIntegrationTests : IClassFixture<WebApplicationFactory<global::Program>>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<global::Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Invariant", "PRC-001")]
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
    [Trait("Invariant", "PRC-011")]
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
    [Trait("Invariant", "PRC-014")]
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

    public sealed record MenuResponse(IReadOnlyList<SizeResponse> Sizes, IReadOnlyList<ToppingResponse> Toppings, decimal TaxRate);
    public sealed record SizeResponse(string Name, decimal Price);
    public sealed record ToppingResponse(string Id, decimal Rate);
    public sealed record PreviewResponse(bool IsValid, PreviewBreakdown? Breakdown, IReadOnlyList<string> Errors);
    public sealed record PreviewBreakdown(decimal Subtotal, decimal Discount, decimal Tax, decimal Total);
}
