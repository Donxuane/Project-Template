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
                    idempotencykey,
                    strategyname,
                    symbol,
                    action,
                    rawsignal,
                    tradingmode,
                    executionintent,
                    side,
                    decisionstatus,
                    guardstage,
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
                    @IdempotencyKey,
                    @StrategyName,
                    @Symbol,
                    @Action,
                    @RawSignal,
                    @TradingMode,
                    @ExecutionIntent,
                    @Side,
                    @DecisionStatus,
                    @GuardStage,
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

    public async Task<TradeExecutionDecisions?> GetLatestByLocalOrderOrCorrelationAsync(
        long localOrderId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
        SELECT
            id AS Id,
            correlationid AS CorrelationId,
            decisionid AS DecisionId,
            idempotencykey AS IdempotencyKey,
            strategyname AS StrategyName,
            symbol AS Symbol,
            action AS Action,
            rawsignal AS RawSignal,
            tradingmode AS TradingMode,
            executionintent AS ExecutionIntent,
            side AS Side,
            decisionstatus AS DecisionStatus,
            guardstage AS GuardStage,
            confidence AS Confidence,
            minconfidence AS MinConfidence,
            reason AS Reason,
            isincooldown AS IsInCooldown,
            cooldownremainingseconds AS CooldownRemainingSeconds,
            cooldownlasttrade AS CooldownLastTrade,
            idempotencyduplicate AS IdempotencyDuplicate,
            riskisallowed AS RiskIsAllowed,
            riskreason AS RiskReason,
            stoplossprice AS StopLossPrice,
            takeprofitprice AS TakeProfitPrice,
            executionsuccess AS ExecutionSuccess,
            localorderid AS LocalOrderId,
            exchangeorderid AS ExchangeOrderId,
            executionerror AS ExecutionError,
            created_at AS Created_At,
            updated_at AS Updated_At
        FROM trade_execution_decisions
        WHERE localorderid = @LocalOrderId
           OR (@CorrelationId IS NOT NULL AND correlationid = @CorrelationId)
        ORDER BY
            CASE WHEN localorderid = @LocalOrderId THEN 0 ELSE 1 END,
            created_at DESC
        LIMIT 1;";

        using var connection = new NpgsqlConnection(connections.Value.MainStorage);
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TradeExecutionDecisions>(
            new CommandDefinition(
                sql,
                new
                {
                    LocalOrderId = localOrderId,
                    CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId
                },
                cancellationToken: cancellationToken));
    }
}
