using System.Text.RegularExpressions;
using LongPizzaOrderBuilder.Pricing;
using Microsoft.AspNetCore.Mvc;

namespace LongPizzaOrderBuilder.OrderService.Controllers;

/// <summary>
/// The customer's door. A customer can send an order, query it and cancel it (cancel only while it is NotStarted).
/// There is deliberately no way to list orders or move an order along here; those are on the restaurant's door.
/// Every call is safe to repeat, because customers retry on unreliable connections (docs/LIFECYCLE.md).
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(OrderStore store) : ControllerBase
{
    private static readonly Regex ValidKey = new("^[A-Za-z0-9_.:-]{1,100}$", RegexOptions.Compiled);

    /// <summary>
    /// Sends an order. Send an <c>Idempotency-Key</c> (a new unique value per order, the same value when retrying): 201 for a new order,
    /// 200 with <c>Idempotency-Replayed: true</c> for a repeat, 422 if the key was used for a different order.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<Order>(StatusCodes.Status201Created)]
    [ProducesResponseType<Order>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public ActionResult<Order> Create(OrderRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { errors = new[] { "The Idempotency-Key header is required. Send a new unique value for each order, and the same value when you retry it." } });
        if (!ValidKey.IsMatch(idempotencyKey))
            return BadRequest(new { errors = new[] { "Idempotency-Key must be 1 to 100 characters: letters, digits, '_', '-', '.' or ':'." } });
        try
        {
            var result = store.Create(request, idempotencyKey);
            if (!result.Replayed) return Created($"/api/orders/{result.Order.Id}", result.Order);
            Response.Headers["Idempotency-Replayed"] = "true";
            return Ok(result.Order);
        }
        catch (PricingValidationException exception)
        {
            return BadRequest(new { errors = exception.Errors });
        }
        catch (IdempotencyConflictException exception)
        {
            return UnprocessableEntity(new { errors = new[] { exception.Message } });
        }
    }

    /// <summary>The order's status, history and whether the customer can still cancel it.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<Order>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Order> Get(string id)
    {
        var order = store.Find(id);
        if (order is null) return NotFound();
        return Ok(order);
    }

    /// <summary>Cancels the order. Only while NotStarted. Repeating it is harmless (200, no change). If the restaurant started first: 409 with the order as it is.</summary>
    [HttpPost("{id}/cancel")]
    [ProducesResponseType<Order>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<Order> Cancel(string id) => Reply(store.Transition(id, OrderStatus.Cancelled, OrderActor.Customer));

    private ActionResult<Order> Reply(ChangeResult result) => result.Outcome switch
    {
        ChangeOutcome.Applied or ChangeOutcome.NoChange => Ok(result.Order),
        ChangeOutcome.NotFound => NotFound(new { errors = new[] { result.Message } }),
        _ => Conflict(new { errors = new[] { result.Message }, order = result.Order }),
    };
}
