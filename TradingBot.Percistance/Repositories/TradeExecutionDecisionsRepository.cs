using System.Data;
using Dapper;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Percistance.Repositories;

public sealed class TradeExecutionDecisionsRepository(IDbConnection connection)
    : ITradeExecutionDecisionsRepository
{
    public async Task<long> AddDesicionAsync(TradeExecutionDecisions decision)
    {
        const string sql = """
            INSERT INTO trade_execution_decisions (
                correlationid, decisionid, strategyname, symbol, action, rawsignal,
                tradingmode, executionintent, side, decisionstatus, guardstage, reason,
                riskisallowed, stoplossprice, takeprofitprice, expectedmovepercent,
                trendconfidencescore, shortmaslopepercent, trendstrengthpercent,
                executionsuccess, localorderid, exchangeorderid, executionerror)
            VALUES (
                @CorrelationId, @DecisionId, @StrategyName, @Symbol, @Action, @RawSignal,
                @TradingMode, @ExecutionIntent, @Side, @DecisionStatus, @GuardStage, @Reason,
                @RiskIsAllowed, @StopLossPrice, @TakeProfitPrice, @ExpectedMovePercent,
                @TrendConfidenceScore, @ShortMaSlopePercent, @TrendStrengthPercent,
                @ExecutionSuccess, @LocalOrderId, @ExchangeOrderId, @ExecutionError)
            RETURNING id;
            """;
        decision.Id = await connection.ExecuteScalarAsync<long>(sql, decision);
        return decision.Id.Value;
    }

    public Task UpdateDesicionAsync(TradeExecutionDecisions decision)
    {
        var properties = typeof(TradeExecutionDecisions)
            .GetProperties()
            .Where(property =>
                property.Name != nameof(TradeExecutionDecisions.Id) &&
                property.GetValue(decision) is not null);
        var assignments = properties.Select(property =>
            $"{property.Name.ToLowerInvariant()} = @{property.Name}");
        var sql = $"""
            UPDATE trade_execution_decisions
            SET {string.Join(", ", assignments)},
                updated_at = now()
            WHERE id = @Id;
            """;
        return connection.ExecuteAsync(sql, decision);
    }
}
