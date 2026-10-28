using Microsoft.AspNetCore.Mvc;

namespace LongPizzaOrderBuilder.Api.Controllers;

[ApiController]
[Route("api/menu")]
public sealed class MenuController : ControllerBase
{
    [HttpGet]
    public ActionResult<PizzaMenu> Get() => Ok(PizzaMenu.Default);
}
