using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.GeneralApis;

namespace TradingBot.Controllers;

[Route("api/[controller]")]
public class ReportingsController(IReportingsRepository reportingsRepository) : ControllerBase
{
    [HttpGet("orderTradeExecutionView")]
    public async Task<ActionResult<List<OrderTradeExecutionView>>> GetOrderView(int pageSize, int pageIndex)
    {
        return await reportingsRepository.GetOrderTradeExecutionViewAsync(pageSize, pageIndex);
    }

    [HttpGet("decisionExecutionView")]
    public async Task<ActionResult<List<DecisionExecutionReportView>>> GetDecisionExecutionView(
        int pageSize,
        int pageIndex,
        [FromQuery] DecisionExecutionReportFilter filter)
    {
        return await reportingsRepository.GetDecisionExecutionReportViewAsync(pageSize, pageIndex, filter);
    }

    [HttpGet("positionsView")]
    public async Task<ActionResult<List<PositionView>>> GetPositionView(int pageSize, int pageIndex) =>
        await reportingsRepository.GetPositionViewsAsync(pageSize, pageIndex);
}
