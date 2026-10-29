using LongPizzaOrderBuilder.Pricing;

// The order builder's API: the menu and the live price preview. It holds no orders.
// Orders are sent to the separate order service (src/backend/LongPizzaOrderBuilder.OrderService).
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton(PizzaMenu.Default);
builder.Services.AddSingleton<PricingService>();

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseCors("frontend");
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();

public partial class Program { }
