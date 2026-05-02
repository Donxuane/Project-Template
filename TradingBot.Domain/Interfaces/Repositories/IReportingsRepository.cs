using Microsoft.AspNetCore.Mvc.RazorPages;
using TradingBot.Domain.Models.GeneralApis;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface IReportingsRepository
{
    public Task<List<OrderTradeExecutionView>> GetOrderTradeExecutionViewAsync(int pageSize, int pageIndex);
    public Task<List<DecisionExecutionReportView>> GetDecisionExecutionReportViewAsync(int pageSize, int pageIndex, DecisionExecutionReportFilter? filter = null);
    public Task<List<PositionView>> GetPositionViewsAsync(int pageSize, int pageIndex);
}
