using System.Text.RegularExpressions;
using LongPizzaOrderBuilder.Pricing;
using Microsoft.AspNetCore.Mvc;

namespace LongPizzaOrderBuilder.OrderService.Controllers;

/// <summary>
/// The customer's door. A customer can send an order, query it, update it and cancel it, the last two only while it is NotStarted.
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
            SetETag(result.Order);
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

    /// <summary>The order's status, version, history and whether the customer can still change it. The <c>ETag</c> header is its version.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<Order>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Order> Get(string id)
    {
        var order = store.Find(id);
        if (order is null) return NotFound();
        SetETag(order);
        return Ok(order);
    }

    /// <summary>
    /// Replaces the order's contents. Only while NotStarted. <c>If-Match</c> must carry the version the customer is changing (the ETag they last saw).
    /// 200 on success (also when the same update is sent again), 409 if the restaurant has started (the body carries the order as it is),
    /// 412 if the order changed since that version, 428 if If-Match is missing.
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType<Order>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public ActionResult<Order> Update(string id, OrderRequest request, [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
            return StatusCode(StatusCodes.Status428PreconditionRequired, new { errors = new[] { "The If-Match header is required. Send the version of the order you are changing (the ETag you last received)." } });
        if (!int.TryParse(ifMatch.Trim().Replace("W/", "").Trim('"'), out var version) || version < 1)
            return BadRequest(new { errors = new[] { "If-Match must be the order's version, for example \"2\"." } });
        try
        {
            return Reply(store.Update(id, request, version));
        }
        catch (PricingValidationException exception)
        {
            return BadRequest(new { errors = exception.Errors });
        }
    }

    /// <summary>Cancels the order. Only while NotStarted. Repeating it is harmless (200, no change). If the restaurant started first: 409 with the order as it is.</summary>
    [HttpPost("{id}/cancel")]
    [ProducesResponseType<Order>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<Order> Cancel(string id) => Reply(store.Transition(id, OrderStatus.Cancelled, OrderActor.Customer));

    private ActionResult<Order> Reply(ChangeResult result)
    {
        switch (result.Outcome)
        {
            case ChangeOutcome.Applied or ChangeOutcome.NoChange:
                SetETag(result.Order!);
                return Ok(result.Order);
            case ChangeOutcome.NotFound:
                return NotFound(new { errors = new[] { result.Message } });
            case ChangeOutcome.Stale:
                SetETag(result.Order!);
                return StatusCode(StatusCodes.Status412PreconditionFailed, new { errors = new[] { result.Message }, order = result.Order });
            default:
                SetETag(result.Order!);
                return Conflict(new { errors = new[] { result.Message }, order = result.Order });
        }
    }

    private void SetETag(Order order) => Response.Headers.ETag = $"\"{order.Version}\"";
}
