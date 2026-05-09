using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Diagnostics;

namespace TradingBot.Controllers;

[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController(ITradingHealthDiagnosticsService diagnosticsService) : ControllerBase
{
    [HttpGet("trading-health")]
    public async Task<ActionResult<TradingRuntimeHealthResult>> GetTradingHealth(
        [FromQuery] int maxBalanceAgeMinutes = 10,
        CancellationToken cancellationToken = default)
    {
        var normalizedMinutes = Math.Max(1, maxBalanceAgeMinutes);
        var result = await diagnosticsService.RunAsync(TimeSpan.FromMinutes(normalizedMinutes), cancellationToken);
        return Ok(result);
    }
}
