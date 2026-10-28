using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using LongPizzaOrderBuilder.OrderService;
using Xunit;

namespace LongPizzaOrderBuilder.IntegrationTests;

/// <summary>
/// ORD-002 across the two processes: the price the order builder previews is the price the order service charges.
/// Pricing is one shared library used by both, and this proves the two running services agree for real requests.
/// </summary>
public sealed class BuilderAndServiceAgreeTests : IClassFixture<WebApplicationFactory<global::Program>>, IClassFixture<WebApplicationFactory<OrderServiceHost>>
{
    private readonly HttpClient _builder;
    private readonly HttpClient _service;

    public BuilderAndServiceAgreeTests(WebApplicationFactory<global::Program> builder, WebApplicationFactory<OrderServiceHost> service)
    {
        _builder = builder.CreateClient();
        _service = service.CreateClient();
    }

    [Fact, Trait("Invariant", "ORD-002")]
    public async Task The_builders_preview_and_the_services_accepted_total_agree_to_the_cent()
    {
        var checks = 0;
        foreach (var promo in new string?[] { null, "ILOVEPIZZA", "SAVE5", "BOGUS" })
            foreach (var tip in new object[] { new { type = "None" }, new { type = "Percent", percent = 18 }, new { type = "Custom", amount = 3.5 } })
                foreach (var quantity in new[] { 1, 3, 10 })
                {
                    var items = new[]
                    {
                        new { size = "Large", crust = "Thin", toppings = new[] { "bacon", "pepperoni" }, quantity },
                        new { size = "Small", crust = "Classic", toppings = Array.Empty<string>(), quantity = 1 },
                    };
                    var preview = await (await _builder.PostAsJsonAsync("/api/pricing/preview", new { items, promoCode = promo, tip }))
                        .Content.ReadFromJsonAsync<Preview>();

                    var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(new { customerName = "Agree", items, promoCode = promo, tip }) };
                    request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                    var order = await (await _service.SendAsync(request)).Content.ReadFromJsonAsync<Accepted>();

                    Assert.Equal(preview!.Breakdown.Total, order!.Total);
                    Assert.Equal(preview.Breakdown.Total, order.Pricing.Total);
                    Assert.Equal(preview.Breakdown.Tax, order.Pricing.Tax);
                    Assert.Equal(preview.Breakdown.Discount, order.Pricing.Discount);
                    checks++;
                }
        Assert.Equal(36, checks);
    }

    public sealed record Preview(Breakdown Breakdown);
    public sealed record Breakdown(decimal Total, decimal Tax, decimal Discount);
    public sealed record Accepted(decimal Total, Breakdown Pricing);
}
