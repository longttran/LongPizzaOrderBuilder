using System.Text.Json.Serialization;
using LongPizzaOrderBuilder.Pricing;

namespace LongPizzaOrderBuilder.OrderService;

/// <summary>Builds the order service. Kept out of Program.cs so tests can start the service the same way the real process does.</summary>
public sealed class OrderServiceHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (builder.Configuration["urls"] is null && builder.Configuration["ASPNETCORE_URLS"] is null)
            builder.WebHost.UseUrls("http://localhost:8090");

        builder.Services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddHealthChecks();

        // The order builder's page runs in a browser on another address, so the browser asks permission first (CORS).
        // The retry header must be allowed, and Idempotency-Replayed must be readable, or the browser would hide them.
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];
        builder.Services.AddCors(options => options.AddPolicy("order-builder", policy => policy
            .WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Idempotency-Replayed")));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(PizzaMenu.Default);
        builder.Services.AddSingleton<PricingService>();
        builder.Services.AddSingleton<OrderStore>();
        builder.Services.AddHostedService<KitchenSimulatorService>();

        var app = builder.Build();
        app.UseCors("order-builder");
        app.MapHealthChecks("/health");
        app.MapControllers();
        return app;
    }
}
