using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Analytics;

namespace TradingBot.Percistance.Repositories;

public class SpotFuturesCrossMarketEvaluationRepository(IDbConnection connection) : ISpotFuturesCrossMarketEvaluationRepository
{
    private const string SelectColumns = """
        SELECT id,
               correlationid                  AS CorrelationId,
               symbol,
               interval,
               candle_open_time               AS CandleOpenTimeUtc,
               candle_close_time              AS CandleCloseTimeUtc,
               spot_close                     AS SpotClose,
               spot_trend_state               AS SpotTrendState,
               spot_trend_confidence_score    AS SpotTrendConfidenceScore,
               spot_short_ma_slope_percent    AS SpotShortMaSlopePercent,
               spot_trend_strength_percent    AS SpotTrendStrengthPercent,
               spot_momentum_percent          AS SpotMomentumPercent,
               futures_close                  AS FuturesClose,
               futures_trend_state            AS FuturesTrendState,
               futures_trend_confidence_score AS FuturesTrendConfidenceScore,
               futures_short_ma_slope_percent AS FuturesShortMaSlopePercent,
               futures_trend_strength_percent AS FuturesTrendStrengthPercent,
               futures_atr_percent            AS FuturesAtrPercent,
               basis_percent                  AS BasisPercent,
               funding_rate                   AS FundingRate,
               mark_price                     AS MarkPrice,
               markets_in_sync                AS MarketsInSync,
               decided_intent                 AS DecidedIntent,
               decision_label                 AS DecisionLabel,
               reason,
               executed,
               position_id                    AS PositionId,
               local_order_id                 AS LocalOrderId,
               created_at                     AS CreatedAtUtc
        FROM spot_futures_cross_market_evaluations
        """;

    public async Task<long> InsertAsync(SpotFuturesCrossMarketEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        evaluation.CreatedAtUtc = DateTime.UtcNow;

        const string sql = """
            INSERT INTO spot_futures_cross_market_evaluations
                (correlationid, symbol, interval, candle_open_time, candle_close_time,
                 spot_close, spot_trend_state, spot_trend_confidence_score, spot_short_ma_slope_percent, spot_trend_strength_percent, spot_momentum_percent,
                 futures_close, futures_trend_state, futures_trend_confidence_score, futures_short_ma_slope_percent, futures_trend_strength_percent, futures_atr_percent,
                 basis_percent, funding_rate, mark_price, markets_in_sync,
                 decided_intent, decision_label, reason, executed, position_id, local_order_id, created_at)
            VALUES
                (@CorrelationId, @Symbol, @Interval, @CandleOpenTimeUtc, @CandleCloseTimeUtc,
                 @SpotClose, @SpotTrendState, @SpotTrendConfidenceScore, @SpotShortMaSlopePercent, @SpotTrendStrengthPercent, @SpotMomentumPercent,
                 @FuturesClose, @FuturesTrendState, @FuturesTrendConfidenceScore, @FuturesShortMaSlopePercent, @FuturesTrendStrengthPercent, @FuturesAtrPercent,
                 @BasisPercent, @FundingRate, @MarkPrice, @MarketsInSync,
                 @DecidedIntent, @DecisionLabel, @Reason, @Executed, @PositionId, @LocalOrderId, @CreatedAtUtc)
            ON CONFLICT (symbol, interval, candle_open_time) DO UPDATE
                SET decided_intent  = EXCLUDED.decided_intent,
                    decision_label  = EXCLUDED.decision_label,
                    reason          = EXCLUDED.reason,
                    executed        = EXCLUDED.executed,
                    position_id     = EXCLUDED.position_id,
                    local_order_id  = EXCLUDED.local_order_id
            RETURNING id;
            """;

        var param = new
        {
            evaluation.CorrelationId,
            Symbol = (int)evaluation.Symbol,
            evaluation.Interval,
            evaluation.CandleOpenTimeUtc,
            evaluation.CandleCloseTimeUtc,
            evaluation.SpotClose,
            SpotTrendState = (int)evaluation.SpotTrendState,
            evaluation.SpotTrendConfidenceScore,
            evaluation.SpotShortMaSlopePercent,
            evaluation.SpotTrendStrengthPercent,
            evaluation.SpotMomentumPercent,
            evaluation.FuturesClose,
            FuturesTrendState = (int)evaluation.FuturesTrendState,
            evaluation.FuturesTrendConfidenceScore,
            evaluation.FuturesShortMaSlopePercent,
            evaluation.FuturesTrendStrengthPercent,
            evaluation.FuturesAtrPercent,
            evaluation.BasisPercent,
            evaluation.FundingRate,
            evaluation.MarkPrice,
            evaluation.MarketsInSync,
            DecidedIntent = (int)evaluation.DecidedIntent,
            evaluation.DecisionLabel,
            evaluation.Reason,
            evaluation.Executed,
            evaluation.PositionId,
            evaluation.LocalOrderId,
            evaluation.CreatedAtUtc
        };

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, param, cancellationToken: cancellationToken));
        evaluation.Id = id;
        return id;
    }

    public async Task<DateTime?> GetLastEvaluatedCandleOpenTimeAsync(TradingSymbol symbol, string interval, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT MAX(candle_open_time)
            FROM spot_futures_cross_market_evaluations
            WHERE symbol = @Symbol AND interval = @Interval;
            """;

        return await connection.ExecuteScalarAsync<DateTime?>(
            new CommandDefinition(sql, new { Symbol = (int)symbol, Interval = interval }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SpotFuturesCrossMarketEvaluation>> GetRecentAsync(TradingSymbol symbol, int limit, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            {SelectColumns}
            WHERE symbol = @Symbol
            ORDER BY candle_open_time DESC
            LIMIT @Limit;
            """;

        var result = await connection.QueryAsync<SpotFuturesCrossMarketEvaluation>(
            new CommandDefinition(sql, new { Symbol = (int)symbol, Limit = Math.Max(1, limit) }, cancellationToken: cancellationToken));
        return result.ToList();
    }
}
