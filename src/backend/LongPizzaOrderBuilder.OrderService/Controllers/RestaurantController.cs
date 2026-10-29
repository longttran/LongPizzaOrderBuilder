using Microsoft.AspNetCore.Mvc;

namespace LongPizzaOrderBuilder.OrderService.Controllers;

/// <param name="To">The status to move the order to: Started, ReadyForPickup or Delivered. Required.</param>
public sealed record TransitionRequest(OrderStatus? To);

/// <summary>
/// The restaurant's door: see the orders and move them along. It is a separate set of routes from the customer's, so a customer
/// cannot do any of this. There is no authentication in this project, so anyone who can reach the service can use these routes;
/// keep them off the public internet (the self-hosted site does not publish them). An accepted limit of the scope (ADR-0009).
/// </summary>
[ApiController]
[Route("api/restaurant/orders")]
public sealed class RestaurantController(OrderStore store) : ControllerBase
{
    /// <summary>All orders, newest first. Optionally only those in one status, for example <c>?status=NotStarted</c>.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<Order>> List([FromQuery] string? status = null)
    {
        if (string.IsNullOrWhiteSpace(status)) return Ok(store.All());
        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var wanted) || !Enum.IsDefined(wanted))
            return BadRequest(new { errors = new[] { "status must be NotStarted, Started, ReadyForPickup, Delivered or Cancelled." } });
        return Ok(store.All().Where(order => order.Status == wanted).ToArray());
    }

    /// <summary>
    /// Moves an order one step: NotStarted to Started, Started to ReadyForPickup, ReadyForPickup to Delivered. Asking for the status
    /// the order already has is harmless (200, no change). A step that is not allowed is 409 with the order as it is. The restaurant cannot cancel.
    /// </summary>
    [HttpPost("{id}/transitions")]
    [ProducesResponseType<Order>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<Order> Transition(string id, TransitionRequest request)
    {
        if (request.To is null)
            return BadRequest(new { errors = new[] { "'to' is required: Started, ReadyForPickup or Delivered." } });

        var result = store.Transition(id, request.To.Value, OrderActor.Restaurant);
        return result.Outcome switch
        {
            ChangeOutcome.Applied or ChangeOutcome.NoChange => Ok(result.Order),
            ChangeOutcome.NotFound => NotFound(new { errors = new[] { result.Message } }),
            _ => Conflict(new { errors = new[] { result.Message }, order = result.Order }),
        };
    }
}
