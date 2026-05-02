using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Text;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Shared.Shared.Settings;

namespace TradingBot.Percistance.Repositories;

public class ReportingsRepository(IOptions<ConnectionStrings> connections) : IReportingsRepository
{
    public async Task<List<OrderTradeExecutionView>> GetOrderTradeExecutionViewAsync(int pageSize, int pageIndex)
    {
        var normalizedPageSize = Math.Max(1, pageSize);
        var normalizedPageIndex = Math.Max(0, pageIndex);
        var offset = normalizedPageIndex * normalizedPageSize;

        var query = @"select 
                        o.exchange_order_id as ExchangeOrderId,
                        o.correlationid as CorrelationId,
                        o.parent_position_id as ParentPositionId,
                        o.order_source as OrderSource,
                        o.close_reason as CloseReason,
                        o.status as Status,
                        o.processing_status as ProcessingStatus,
                        te.exchange_trade_id as ExchangeTradeId,
                        te.symbol as Symbol,
                        te.price as Price,
                        te.quantity as Quantity,
                        te.side as Side,
                        te.executed_at as ExecutedAt,
                        ted.""action"" as DesicionAction,
                        ted.confidence as Confidence,
                        ted.minconfidence as MinConfidence,
                        ted.isincooldown as IsInCooldown,
                        ted.cooldownremainingseconds as CooldownRemainingSeconds,
                        ted.riskisallowed as RiskIsAllowed,
                        ted.riskreason as RiskReason,
                        ted.stoplossprice as StopLossPrice,
                        ted.takeprofitprice as TakeProfitPrice,
                        ted.executionsuccess as ExecutionSuccess,
                        ted.executionerror as ExecutionError
                    from orders o
                    left join trade_executions te on o.id = te.order_id 
                    left join trade_execution_decisions ted on o.id = ted.localorderid
                    order by o.created_at desc
                    limit @PageSize offset @Offset;";
        using var connection = new NpgsqlConnection(connections.Value.MainStorage);
        var result = await connection.QueryAsync<OrderTradeExecutionView>(query, new { PageSize = normalizedPageSize, Offset = offset });
        return result.ToList();
    }

    public async Task<List<PositionView>> GetPositionViewsAsync(int pageSize, int pageIndex)
    {
        var normalizedPageSize = Math.Max(1, pageSize);
        var normalizedPageIndex = Math.Max(0, pageIndex);
        var offset = normalizedPageIndex * normalizedPageSize;

        var query = @"select 
                        *
                        from positions
                        order by created_at desc
                        limit @PageSize offset @Offset;";

        using var connection = new NpgsqlConnection(connections.Value.MainStorage);
        return (await connection.QueryAsync<PositionView>(query, new { PageSize = normalizedPageSize, Offset = offset })).ToList();
    }

    public async Task<List<DecisionExecutionReportView>> GetDecisionExecutionReportViewAsync(int pageSize, int pageIndex, DecisionExecutionReportFilter? filter = null)
    {
        var normalizedPageSize = Math.Max(1, pageSize);
        var normalizedPageIndex = Math.Max(0, pageIndex);
        var offset = normalizedPageIndex * normalizedPageSize;

        var sql = new StringBuilder(
            @"SELECT
                ted.id AS DecisionDbId,
                ted.decisionid AS DecisionId,
                ted.idempotencykey AS IdempotencyKey,
                ted.correlationid AS CorrelationId,
                ted.created_at AS CreatedAt,
                ted.updated_at AS UpdatedAt,
                ted.symbol AS Symbol,
                ted.action AS DecisionAction,
                ted.side AS Side,
                ted.rawsignal AS RawSignal,
                ted.tradingmode AS TradingMode,
                ted.executionintent AS ExecutionIntent,
                ted.strategyname AS StrategyName,
                ted.confidence AS Confidence,
                ted.minconfidence AS MinConfidence,
                ted.reason AS Reason,
                ted.decisionstatus AS DecisionStatus,
                ted.guardstage AS GuardStage,
                ted.isincooldown AS IsInCooldown,
                ted.cooldownremainingseconds AS CooldownRemainingSeconds,
                ted.cooldownlasttrade AS CooldownLastTrade,
                ted.idempotencyduplicate AS IdempotencyDuplicate,
                ted.riskisallowed AS RiskIsAllowed,
                ted.riskreason AS RiskReason,
                ted.stoplossprice AS StopLossPrice,
                ted.takeprofitprice AS TakeProfitPrice,
                ted.executionsuccess AS ExecutionSuccess,
                ted.executionerror AS ExecutionError,
                ted.localorderid AS LocalOrderId,
                ted.exchangeorderid AS ExchangeOrderId,
                o.status AS OrderStatus,
                o.processing_status AS ProcessingStatus,
                o.order_source AS OrderSource,
                o.close_reason AS CloseReason,
                o.parent_position_id AS ParentPositionId,
                o.correlationid AS OrderCorrelationId,
                te.exchange_trade_id AS ExchangeTradeId,
                te.executed_at AS ExecutedAt,
                te.price AS ExecutionPrice,
                te.quantity AS ExecutedQuantity,
                te.fee AS Fees,
                p.id AS PositionId,
                p.realized_pnl AS RealizedPnl,
                p.unrealized_pnl AS UnrealizedPnl,
                p.exit_reason AS ExitReason,
                p.is_open AS IsOpen
            FROM trade_execution_decisions ted
            LEFT JOIN orders o ON o.id = ted.localorderid
            LEFT JOIN trade_executions te ON te.order_id = o.id
            LEFT JOIN positions p ON p.id = o.parent_position_id
            WHERE 1 = 1");

        var parameters = new DynamicParameters();
        if (filter is not null)
        {
            if (filter.Symbol.HasValue)
            {
                sql.Append(" AND ted.symbol = @Symbol");
                parameters.Add("Symbol", (int)filter.Symbol.Value);
            }
            if (filter.FromDateUtc.HasValue)
            {
                sql.Append(" AND ted.created_at >= @FromDateUtc");
                parameters.Add("FromDateUtc", filter.FromDateUtc.Value);
            }
            if (filter.ToDateUtc.HasValue)
            {
                sql.Append(" AND ted.created_at <= @ToDateUtc");
                parameters.Add("ToDateUtc", filter.ToDateUtc.Value);
            }
            if (!string.IsNullOrWhiteSpace(filter.StrategyName))
            {
                sql.Append(" AND ted.strategyname ILIKE @StrategyName");
                parameters.Add("StrategyName", filter.StrategyName.Trim());
            }
            if (filter.TradingMode.HasValue)
            {
                sql.Append(" AND ted.tradingmode = @TradingMode");
                parameters.Add("TradingMode", (int)filter.TradingMode.Value);
            }
            if (filter.ExecutionIntent.HasValue)
            {
                sql.Append(" AND ted.executionintent = @ExecutionIntent");
                parameters.Add("ExecutionIntent", (int)filter.ExecutionIntent.Value);
            }
            if (filter.OnlyExecuted == true)
                sql.Append(" AND ted.executionsuccess = TRUE");
            if (filter.OnlySkipped == true)
                sql.Append(" AND ted.decisionstatus = 1");
            if (filter.OnlyFailed == true)
                sql.Append(" AND ted.decisionstatus = 3");
            if (filter.OnlyCooldownBlocked == true)
                sql.Append(" AND ted.guardstage = 1");
            if (filter.OnlyIdempotencyDuplicates == true)
                sql.Append(" AND ted.idempotencyduplicate = TRUE");
            if (filter.BlockedBy.HasValue)
            {
                sql.Append(" AND ted.guardstage = @GuardStage");
                parameters.Add("GuardStage", (int)filter.BlockedBy.Value);
            }
            if (filter.OrderSource.HasValue)
            {
                sql.Append(" AND o.order_source = @OrderSource");
                parameters.Add("OrderSource", (int)filter.OrderSource.Value);
            }
            if (filter.CloseReason.HasValue)
            {
                sql.Append(" AND o.close_reason = @CloseReason");
                parameters.Add("CloseReason", (int)filter.CloseReason.Value);
            }
        }

        sql.Append(" ORDER BY ted.created_at DESC LIMIT @PageSize OFFSET @Offset;");
        parameters.Add("PageSize", normalizedPageSize);
        parameters.Add("Offset", offset);

        using var connection = new NpgsqlConnection(connections.Value.MainStorage);
        var result = await connection.QueryAsync<DecisionExecutionReportView>(sql.ToString(), parameters);
        return result.ToList();
    }
}
