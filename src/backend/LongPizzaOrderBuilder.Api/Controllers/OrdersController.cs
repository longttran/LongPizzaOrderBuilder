using Microsoft.AspNetCore.Mvc;

namespace LongPizzaOrderBuilder.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(PizzaOrderStore store) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<PizzaOrder>> Get() => Ok(store.All());

    [HttpGet("{id}")]
    [ProducesResponseType<PizzaOrder>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<PizzaOrder> GetById(string id)
    {
        var order = store.Find(id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    [ProducesResponseType<PizzaOrder>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<PizzaOrder> Create(CreatePizzaOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            return BadRequest(new { errors = new[] { "Customer name is required." } });
        if (request.CustomerName.Trim().Length > 80)
            return BadRequest(new { errors = new[] { "Customer name must be 80 characters or fewer." } });
        try
        {
            var order = store.Add(request);
            return Created($"/api/orders/{order.Id}", order);
        }
        catch (PricingValidationException exception)
        {
            return BadRequest(new { errors = exception.Errors });
        }
    }
}
