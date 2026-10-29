using Microsoft.AspNetCore.Mvc;

using LongPizzaOrderBuilder.Pricing;

namespace LongPizzaOrderBuilder.Api.Controllers;

[ApiController]
[Route("api/menu")]
public sealed class MenuController(PizzaMenu menu) : ControllerBase
{
    [HttpGet]
    public ActionResult<PizzaMenu> Get() => Ok(menu);
}
