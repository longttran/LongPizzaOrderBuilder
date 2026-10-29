namespace LongPizzaOrderBuilder.OrderService;

/// <summary>
/// A stand-in for the restaurant, for demos and tests: after an order has waited <c>delay</c> in its current status, the simulator
/// moves it one step along. It uses the same checked path as every other move (<see cref="OrderStore.Transition"/>), so a customer's
/// cancel and the timer cannot both win. The real restaurant side is the endpoint under <c>/api/restaurant</c>, or <c>scripts/restaurant.sh</c>.
/// </summary>
public static class KitchenSimulator
{
    /// <summary>The status this order should move to now, or null if it is not due (or the simulation is off).</summary>
    public static OrderStatus? DueMove(Order order, DateTimeOffset now, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) return null;
        var next = OrderLifecycle.NextStatus(order.Status);
        return next is not null && now - order.UpdatedAt >= delay ? next : null;
    }

    /// <summary>Moves every due order one step. Returns how many moved.</summary>
    public static int RunOnce(OrderStore store, TimeProvider time, TimeSpan delay)
    {
        var now = time.GetUtcNow();
        var moved = 0;
        foreach (var order in store.All())
        {
            var next = DueMove(order, now, delay);
            if (next is not null && store.Transition(order.Id, next.Value, OrderActor.Restaurant).Outcome == ChangeOutcome.Applied) moved++;
        }
        return moved;
    }
}

/// <summary>Runs <see cref="KitchenSimulator"/> every second when <c>Restaurant:AutoAdvanceSeconds</c> is above zero. Off by default.</summary>
public sealed class KitchenSimulatorService(OrderStore store, TimeProvider time, IConfiguration configuration, ILogger<KitchenSimulatorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue<double>("Restaurant:AutoAdvanceSeconds");
        if (seconds <= 0)
        {
            logger.LogInformation("Restaurant simulation is off. Move orders with POST /api/restaurant/orders/{{id}}/transitions or scripts/restaurant.sh.");
            return;
        }
        var delay = TimeSpan.FromSeconds(seconds);
        logger.LogInformation("Restaurant simulation is on: each order moves one step after waiting {Seconds} s in its status.", seconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            KitchenSimulator.RunOnce(store, time, delay);
    }
}
