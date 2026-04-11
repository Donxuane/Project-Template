using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Transactions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Decision;
using TradingBot.Shared.Shared.Settings;

namespace TradingBot.Percistance.Repositories;

public class TradeExecutionDesicionsRepository(IOptions<ConnectionStrings> connections) : ITradeExecutionDesicionsRepository
{
    public async Task<long> AddDesicionAsync(TradeExecutionDecisions desicion)
    {
        const string sql = @"
                INSERT INTO trade_execution_decisions (
                    correlationid,
                    decisionid,
                    symbol,
                    action,
                    confidence,
                    minconfidence,
                    reason,
                    isincooldown,
                    cooldownremainingseconds,
                    cooldownlasttrade,
                    idempotencyduplicate,
                    riskisallowed,
                    riskreason,
                    stoplossprice,
                    takeprofitprice,
                    executionsuccess,
                    localorderid,
                    exchangeorderid,
                    executionerror
                )
                VALUES (
                    @CorrelationId,
                    @DecisionId,
                    @Symbol,
                    @Action,
                    @Confidence,
                    @MinConfidence,
                    @Reason,
                    @IsInCooldown,
                    @CooldownRemainingSeconds,
                    @CooldownLastTrade,
                    @IdempotencyDuplicate,
                    @RiskIsAllowed,
                    @RiskReason,
                    @StopLossPrice,
                    @TakeProfitPrice,
                    @ExecutionSuccess,
                    @LocalOrderId,
                    @ExchangeOrderId,
                    @ExecutionError
                 )
                    RETURNING id;";
        using var connection = new NpgsqlConnection(connections.Value.MainStorage);
        await connection.OpenAsync();
        var id = await connection.ExecuteScalarAsync<long>(sql, desicion);
        desicion.Id = id;
        return id;
    }

    public async Task UpdateDesicionAsync(TradeExecutionDecisions desicion)
    {
        var properties = typeof(TradeExecutionDecisions)
       .GetProperties()
       .Where(p =>
           p.Name != nameof(TradeExecutionDecisions.Id) &&
           p.Name != nameof(TradeExecutionDecisions.Created_At) &&
           p.GetValue(desicion) != null);

        var setClauses = properties
            .Select(p => $"{p.Name.Trim().ToLower()} = @{p.Name}");

        var sql = $@"
        UPDATE trade_execution_decisions
        SET {string.Join(", ", setClauses)},
            updated_at = now()
        WHERE id = @Id;";

        using var connection = new NpgsqlConnection(connections.Value.MainStorage);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, desicion);
    }
}
