using Microsoft.AspNetCore.Mvc;

namespace LongPizzaOrderBuilder.Api.Controllers;

[ApiController]
[Route("api/pricing")]
public sealed class PricingController(PricingService pricing) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<PricingPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<PricingPreviewResponse>(StatusCodes.Status400BadRequest)]
    public ActionResult<PricingPreviewResponse> Preview(PricingPreviewRequest request)
    {
        var response = pricing.Calculate(request);
        return response.IsValid ? Ok(response) : BadRequest(response);
    }
}
