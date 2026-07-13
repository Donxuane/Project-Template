using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public sealed class AdaptiveRollingProfitExitRepository(IDbConnection connection) : IAdaptiveRollingProfitExitRepository
{
    public async Task<FuturesCommissionRate?> GetLatestCommissionRateAsync(
        string applicationId,
        string executionEnvironment,
        string accountKey,
        TradingSymbol symbol,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   application_id AS ApplicationId,
                   execution_environment AS ExecutionEnvironment,
                   account_key AS AccountKey,
                   symbol,
                   maker_commission_rate AS MakerCommissionRate,
                   taker_commission_rate AS TakerCommissionRate,
                   rpi_commission_rate AS RpiCommissionRate,
                   source,
                   is_fallback AS IsFallback,
                   fallback_reason AS FallbackReason,
                   fetched_at AS FetchedAtUtc,
                   created_at AS CreatedAt
            FROM futures_commission_rates
            WHERE application_id = @ApplicationId
              AND execution_environment = @ExecutionEnvironment
              AND account_key = @AccountKey
              AND symbol = @Symbol
            ORDER BY fetched_at DESC, id DESC
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<FuturesCommissionRate>(
            new CommandDefinition(
                sql,
                new
                {
                    ApplicationId = applicationId,
                    ExecutionEnvironment = executionEnvironment,
                    AccountKey = accountKey,
                    Symbol = (int)symbol
                },
                cancellationToken: cancellationToken));
    }

    public async Task InsertCommissionRateAsync(FuturesCommissionRate rate, CancellationToken cancellationToken = default)
    {
        rate.CreatedAt = DateTime.UtcNow;
        if (rate.FetchedAtUtc == default)
            rate.FetchedAtUtc = rate.CreatedAt;

        const string sql = """
            INSERT INTO futures_commission_rates
                (application_id, execution_environment, account_key, symbol, maker_commission_rate, taker_commission_rate,
                 rpi_commission_rate, source, is_fallback, fallback_reason, fetched_at, created_at)
            VALUES
                (@ApplicationId, @ExecutionEnvironment, @AccountKey, @Symbol, @MakerCommissionRate, @TakerCommissionRate,
                 @RpiCommissionRate, @Source, @IsFallback, @FallbackReason, @FetchedAtUtc, @CreatedAt)
            RETURNING id;
            """;

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                sql,
                new
                {
                    rate.ApplicationId,
                    rate.ExecutionEnvironment,
                    rate.AccountKey,
                    Symbol = (int)rate.Symbol,
                    rate.MakerCommissionRate,
                    rate.TakerCommissionRate,
                    rate.RpiCommissionRate,
                    rate.Source,
                    rate.IsFallback,
                    rate.FallbackReason,
                    rate.FetchedAtUtc,
                    rate.CreatedAt
                },
                cancellationToken: cancellationToken));
        rate.Id = id;
    }

    public async Task<AdaptiveRollingProfitExitStateRecord?> GetStateAsync(long positionId, CancellationToken cancellationToken = default)
    {
        const string sql = StateSelectSql + """
            WHERE position_id = @PositionId;
            """;

        return await connection.QuerySingleOrDefaultAsync<AdaptiveRollingProfitExitStateRecord>(
            new CommandDefinition(sql, new { PositionId = positionId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AdaptiveRollingProfitExitStateRecord>> GetOpenStatesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = StateSelectSql + """
            WHERE state <> @ClosedState
            ORDER BY updated_at DESC;
            """;

        var rows = await connection.QueryAsync<AdaptiveRollingProfitExitStateRecord>(
            new CommandDefinition(
                sql,
                new { ClosedState = (int)AdaptiveRollingProfitExitState.Closed },
                cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task UpsertStateAsync(AdaptiveRollingProfitExitStateRecord state, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (state.CreatedAt == default)
            state.CreatedAt = now;
        state.UpdatedAt = now;

        const string sql = """
            INSERT INTO adaptive_rolling_profit_exit_states
            (
                position_id, symbol, side, state, remaining_quantity, entry_price, entry_notional,
                original_stop_loss_price, original_take_profit_price, current_stop_loss_price, current_take_profit_price,
                original_max_hold_until, eligible_since, consecutive_profitable_observations, armed_at,
                arming_executable_price, arming_projected_net_pnl, arming_fee_snapshot_json,
                arming_trend_flow_snapshot_json, peak_projected_net_pnl, best_executable_price,
                peak_updated_at, last_peak_persisted_at, last_projected_net_pnl, last_gross_projected_pnl,
                last_estimated_exit_fee, last_actual_entry_fee, last_funding, last_adverse_move_reserve,
                last_spread_bps, last_estimated_slippage_bps, last_trend_flow_score, last_fee_source,
                last_fee_age_seconds, last_decision, last_rejection_reason, last_evaluated_at,
                last_transition_at, close_correlation_id, close_local_order_id, close_exchange_order_id,
                close_submitted_at, close_acknowledged_at, close_filled_at, actual_realized_gross_pnl,
                actual_realized_net_pnl, created_at, updated_at
            )
            VALUES
            (
                @PositionId, @Symbol, @Side, @State, @RemainingQuantity, @EntryPrice, @EntryNotional,
                @OriginalStopLossPrice, @OriginalTakeProfitPrice, @CurrentStopLossPrice, @CurrentTakeProfitPrice,
                @OriginalMaxHoldUntilUtc, @EligibleSinceUtc, @ConsecutiveProfitableObservations, @ArmedAtUtc,
                @ArmingExecutablePrice, @ArmingProjectedNetPnl, CAST(@ArmingFeeSnapshotJson AS jsonb),
                CAST(@ArmingTrendFlowSnapshotJson AS jsonb), @PeakProjectedNetPnl, @BestExecutablePrice,
                @PeakUpdatedAtUtc, @LastPeakPersistedAtUtc, @LastProjectedNetPnl, @LastGrossProjectedPnl,
                @LastEstimatedExitFee, @LastActualEntryFee, @LastFunding, @LastAdverseMoveReserve,
                @LastSpreadBps, @LastEstimatedSlippageBps, @LastTrendFlowScore, @LastFeeSource,
                @LastFeeAgeSeconds, @LastDecision, @LastRejectionReason, @LastEvaluatedAtUtc,
                @LastTransitionAtUtc, @CloseCorrelationId, @CloseLocalOrderId, @CloseExchangeOrderId,
                @CloseSubmittedAtUtc, @CloseAcknowledgedAtUtc, @CloseFilledAtUtc, @ActualRealizedGrossPnl,
                @ActualRealizedNetPnl, @CreatedAt, @UpdatedAt
            )
            ON CONFLICT (position_id) DO UPDATE SET
                symbol = EXCLUDED.symbol,
                side = EXCLUDED.side,
                state = EXCLUDED.state,
                remaining_quantity = EXCLUDED.remaining_quantity,
                entry_price = EXCLUDED.entry_price,
                entry_notional = EXCLUDED.entry_notional,
                original_stop_loss_price = EXCLUDED.original_stop_loss_price,
                original_take_profit_price = EXCLUDED.original_take_profit_price,
                current_stop_loss_price = EXCLUDED.current_stop_loss_price,
                current_take_profit_price = EXCLUDED.current_take_profit_price,
                original_max_hold_until = EXCLUDED.original_max_hold_until,
                eligible_since = EXCLUDED.eligible_since,
                consecutive_profitable_observations = EXCLUDED.consecutive_profitable_observations,
                armed_at = EXCLUDED.armed_at,
                arming_executable_price = EXCLUDED.arming_executable_price,
                arming_projected_net_pnl = EXCLUDED.arming_projected_net_pnl,
                arming_fee_snapshot_json = EXCLUDED.arming_fee_snapshot_json,
                arming_trend_flow_snapshot_json = EXCLUDED.arming_trend_flow_snapshot_json,
                peak_projected_net_pnl = GREATEST(adaptive_rolling_profit_exit_states.peak_projected_net_pnl, EXCLUDED.peak_projected_net_pnl),
                best_executable_price = EXCLUDED.best_executable_price,
                peak_updated_at = EXCLUDED.peak_updated_at,
                last_peak_persisted_at = EXCLUDED.last_peak_persisted_at,
                last_projected_net_pnl = EXCLUDED.last_projected_net_pnl,
                last_gross_projected_pnl = EXCLUDED.last_gross_projected_pnl,
                last_estimated_exit_fee = EXCLUDED.last_estimated_exit_fee,
                last_actual_entry_fee = EXCLUDED.last_actual_entry_fee,
                last_funding = EXCLUDED.last_funding,
                last_adverse_move_reserve = EXCLUDED.last_adverse_move_reserve,
                last_spread_bps = EXCLUDED.last_spread_bps,
                last_estimated_slippage_bps = EXCLUDED.last_estimated_slippage_bps,
                last_trend_flow_score = EXCLUDED.last_trend_flow_score,
                last_fee_source = EXCLUDED.last_fee_source,
                last_fee_age_seconds = EXCLUDED.last_fee_age_seconds,
                last_decision = EXCLUDED.last_decision,
                last_rejection_reason = EXCLUDED.last_rejection_reason,
                last_evaluated_at = EXCLUDED.last_evaluated_at,
                last_transition_at = EXCLUDED.last_transition_at,
                close_correlation_id = EXCLUDED.close_correlation_id,
                close_local_order_id = EXCLUDED.close_local_order_id,
                close_exchange_order_id = EXCLUDED.close_exchange_order_id,
                close_submitted_at = EXCLUDED.close_submitted_at,
                close_acknowledged_at = EXCLUDED.close_acknowledged_at,
                close_filled_at = EXCLUDED.close_filled_at,
                actual_realized_gross_pnl = EXCLUDED.actual_realized_gross_pnl,
                actual_realized_net_pnl = EXCLUDED.actual_realized_net_pnl,
                updated_at = EXCLUDED.updated_at;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, ToStateParam(state), cancellationToken: cancellationToken));
    }

    public async Task InsertEvaluationAsync(AdaptiveRollingProfitExitEvaluationRecord evaluation, CancellationToken cancellationToken = default)
    {
        evaluation.CreatedAt = DateTime.UtcNow;
        if (evaluation.EvaluatedAtUtc == default)
            evaluation.EvaluatedAtUtc = evaluation.CreatedAt;

        const string sql = """
            INSERT INTO adaptive_rolling_profit_exit_evaluations
            (
                position_id, symbol, side, state, remaining_quantity, estimated_executable_price,
                gross_projected_pnl, actual_entry_commissions, estimated_exit_commission, funding,
                adverse_move_reserve, projected_net_pnl, break_even_executable_price, peak_projected_net_pnl,
                giveback_amount, giveback_percent, spread_bps, estimated_slippage_bps, top_bid_notional,
                top_ask_notional, order_book_imbalance, microprice, aggressive_buy_quantity,
                aggressive_sell_quantity, aggressive_flow_imbalance, normalized_velocity_bps,
                realized_volatility_bps, trend_flow_score, market_data_event_time,
                market_data_transaction_time, market_data_local_receipt_time, evaluated_at,
                market_data_age_ms, stream_latency_ms, is_market_data_fresh, decision,
                rejection_reason, snapshot_json, created_at
            )
            VALUES
            (
                @PositionId, @Symbol, @Side, @State, @RemainingQuantity, @EstimatedExecutablePrice,
                @GrossProjectedPnl, @ActualEntryCommissions, @EstimatedExitCommission, @Funding,
                @AdverseMoveReserve, @ProjectedNetPnl, @BreakEvenExecutablePrice, @PeakProjectedNetPnl,
                @GivebackAmount, @GivebackPercent, @SpreadBps, @EstimatedSlippageBps, @TopBidNotional,
                @TopAskNotional, @OrderBookImbalance, @Microprice, @AggressiveBuyQuantity,
                @AggressiveSellQuantity, @AggressiveFlowImbalance, @NormalizedVelocityBps,
                @RealizedVolatilityBps, @TrendFlowScore, @MarketDataEventTimeUtc,
                @MarketDataTransactionTimeUtc, @MarketDataLocalReceiptUtc, @EvaluatedAtUtc,
                @MarketDataAgeMs, @StreamLatencyMs, @IsMarketDataFresh, @Decision,
                @RejectionReason, CAST(@SnapshotJson AS jsonb), @CreatedAt
            )
            RETURNING id;
            """;

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, ToEvaluationParam(evaluation), cancellationToken: cancellationToken));
        evaluation.Id = id;
    }

    public async Task UpsertCounterfactualAsync(AdaptiveRollingProfitCounterfactualRecord counterfactual, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (counterfactual.CreatedAt == default)
            counterfactual.CreatedAt = now;
        counterfactual.UpdatedAt = now;

        const string sql = """
            INSERT INTO adaptive_rolling_profit_counterfactuals
            (
                position_id, symbol, side, entry_price, quantity, original_stop_loss_price,
                original_take_profit_price, original_max_hold_until, actual_rolling_exit_price,
                actual_rolling_net_pnl, actual_rolling_closed_at, max_additional_favorable_move,
                max_avoided_adverse_move, counterfactual_exit_price, counterfactual_net_pnl,
                counterfactual_exit_reason, better_exit_method, is_active, completed_at, created_at, updated_at
            )
            VALUES
            (
                @PositionId, @Symbol, @Side, @EntryPrice, @Quantity, @OriginalStopLossPrice,
                @OriginalTakeProfitPrice, @OriginalMaxHoldUntilUtc, @ActualRollingExitPrice,
                @ActualRollingNetPnl, @ActualRollingClosedAtUtc, @MaxAdditionalFavorableMove,
                @MaxAvoidedAdverseMove, @CounterfactualExitPrice, @CounterfactualNetPnl,
                @CounterfactualExitReason, @BetterExitMethod, @IsActive, @CompletedAtUtc, @CreatedAt, @UpdatedAt
            )
            ON CONFLICT (position_id) DO UPDATE SET
                max_additional_favorable_move = GREATEST(adaptive_rolling_profit_counterfactuals.max_additional_favorable_move, EXCLUDED.max_additional_favorable_move),
                max_avoided_adverse_move = GREATEST(adaptive_rolling_profit_counterfactuals.max_avoided_adverse_move, EXCLUDED.max_avoided_adverse_move),
                counterfactual_exit_price = EXCLUDED.counterfactual_exit_price,
                counterfactual_net_pnl = EXCLUDED.counterfactual_net_pnl,
                counterfactual_exit_reason = EXCLUDED.counterfactual_exit_reason,
                better_exit_method = EXCLUDED.better_exit_method,
                is_active = EXCLUDED.is_active,
                completed_at = EXCLUDED.completed_at,
                updated_at = EXCLUDED.updated_at;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, ToCounterfactualParam(counterfactual), cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AdaptiveRollingProfitCounterfactualRecord>> GetActiveCounterfactualsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT position_id AS PositionId,
                   symbol,
                   side,
                   entry_price AS EntryPrice,
                   quantity,
                   original_stop_loss_price AS OriginalStopLossPrice,
                   original_take_profit_price AS OriginalTakeProfitPrice,
                   original_max_hold_until AS OriginalMaxHoldUntilUtc,
                   actual_rolling_exit_price AS ActualRollingExitPrice,
                   actual_rolling_net_pnl AS ActualRollingNetPnl,
                   actual_rolling_closed_at AS ActualRollingClosedAtUtc,
                   max_additional_favorable_move AS MaxAdditionalFavorableMove,
                   max_avoided_adverse_move AS MaxAvoidedAdverseMove,
                   counterfactual_exit_price AS CounterfactualExitPrice,
                   counterfactual_net_pnl AS CounterfactualNetPnl,
                   counterfactual_exit_reason AS CounterfactualExitReason,
                   better_exit_method AS BetterExitMethod,
                   is_active AS IsActive,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt,
                   completed_at AS CompletedAtUtc
            FROM adaptive_rolling_profit_counterfactuals
            WHERE is_active = TRUE
            ORDER BY updated_at ASC;
            """;

        var rows = await connection.QueryAsync<AdaptiveRollingProfitCounterfactualRecord>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private const string StateSelectSql = """
        SELECT position_id AS PositionId,
               symbol,
               side,
               state,
               remaining_quantity AS RemainingQuantity,
               entry_price AS EntryPrice,
               entry_notional AS EntryNotional,
               original_stop_loss_price AS OriginalStopLossPrice,
               original_take_profit_price AS OriginalTakeProfitPrice,
               current_stop_loss_price AS CurrentStopLossPrice,
               current_take_profit_price AS CurrentTakeProfitPrice,
               original_max_hold_until AS OriginalMaxHoldUntilUtc,
               eligible_since AS EligibleSinceUtc,
               consecutive_profitable_observations AS ConsecutiveProfitableObservations,
               armed_at AS ArmedAtUtc,
               arming_executable_price AS ArmingExecutablePrice,
               arming_projected_net_pnl AS ArmingProjectedNetPnl,
               arming_fee_snapshot_json AS ArmingFeeSnapshotJson,
               arming_trend_flow_snapshot_json AS ArmingTrendFlowSnapshotJson,
               peak_projected_net_pnl AS PeakProjectedNetPnl,
               best_executable_price AS BestExecutablePrice,
               peak_updated_at AS PeakUpdatedAtUtc,
               last_peak_persisted_at AS LastPeakPersistedAtUtc,
               last_projected_net_pnl AS LastProjectedNetPnl,
               last_gross_projected_pnl AS LastGrossProjectedPnl,
               last_estimated_exit_fee AS LastEstimatedExitFee,
               last_actual_entry_fee AS LastActualEntryFee,
               last_funding AS LastFunding,
               last_adverse_move_reserve AS LastAdverseMoveReserve,
               last_spread_bps AS LastSpreadBps,
               last_estimated_slippage_bps AS LastEstimatedSlippageBps,
               last_trend_flow_score AS LastTrendFlowScore,
               last_fee_source AS LastFeeSource,
               last_fee_age_seconds AS LastFeeAgeSeconds,
               last_decision AS LastDecision,
               last_rejection_reason AS LastRejectionReason,
               last_evaluated_at AS LastEvaluatedAtUtc,
               last_transition_at AS LastTransitionAtUtc,
               close_correlation_id AS CloseCorrelationId,
               close_local_order_id AS CloseLocalOrderId,
               close_exchange_order_id AS CloseExchangeOrderId,
               close_submitted_at AS CloseSubmittedAtUtc,
               close_acknowledged_at AS CloseAcknowledgedAtUtc,
               close_filled_at AS CloseFilledAtUtc,
               actual_realized_gross_pnl AS ActualRealizedGrossPnl,
               actual_realized_net_pnl AS ActualRealizedNetPnl,
               created_at AS CreatedAt,
               updated_at AS UpdatedAt
        FROM adaptive_rolling_profit_exit_states
        """ + "\n";

    private static object ToStateParam(AdaptiveRollingProfitExitStateRecord state) => new
    {
        state.PositionId,
        Symbol = (int)state.Symbol,
        Side = (int)state.Side,
        State = (int)state.State,
        state.RemainingQuantity,
        state.EntryPrice,
        state.EntryNotional,
        state.OriginalStopLossPrice,
        state.OriginalTakeProfitPrice,
        state.CurrentStopLossPrice,
        state.CurrentTakeProfitPrice,
        state.OriginalMaxHoldUntilUtc,
        state.EligibleSinceUtc,
        state.ConsecutiveProfitableObservations,
        state.ArmedAtUtc,
        state.ArmingExecutablePrice,
        state.ArmingProjectedNetPnl,
        state.ArmingFeeSnapshotJson,
        state.ArmingTrendFlowSnapshotJson,
        state.PeakProjectedNetPnl,
        state.BestExecutablePrice,
        state.PeakUpdatedAtUtc,
        state.LastPeakPersistedAtUtc,
        state.LastProjectedNetPnl,
        state.LastGrossProjectedPnl,
        state.LastEstimatedExitFee,
        state.LastActualEntryFee,
        state.LastFunding,
        state.LastAdverseMoveReserve,
        state.LastSpreadBps,
        state.LastEstimatedSlippageBps,
        state.LastTrendFlowScore,
        state.LastFeeSource,
        state.LastFeeAgeSeconds,
        state.LastDecision,
        state.LastRejectionReason,
        state.LastEvaluatedAtUtc,
        state.LastTransitionAtUtc,
        state.CloseCorrelationId,
        state.CloseLocalOrderId,
        state.CloseExchangeOrderId,
        state.CloseSubmittedAtUtc,
        state.CloseAcknowledgedAtUtc,
        state.CloseFilledAtUtc,
        state.ActualRealizedGrossPnl,
        state.ActualRealizedNetPnl,
        state.CreatedAt,
        state.UpdatedAt
    };

    private static object ToEvaluationParam(AdaptiveRollingProfitExitEvaluationRecord evaluation) => new
    {
        evaluation.PositionId,
        Symbol = (int)evaluation.Symbol,
        Side = (int)evaluation.Side,
        State = (int)evaluation.State,
        evaluation.RemainingQuantity,
        evaluation.EstimatedExecutablePrice,
        evaluation.GrossProjectedPnl,
        evaluation.ActualEntryCommissions,
        evaluation.EstimatedExitCommission,
        evaluation.Funding,
        evaluation.AdverseMoveReserve,
        evaluation.ProjectedNetPnl,
        evaluation.BreakEvenExecutablePrice,
        evaluation.PeakProjectedNetPnl,
        evaluation.GivebackAmount,
        evaluation.GivebackPercent,
        evaluation.SpreadBps,
        evaluation.EstimatedSlippageBps,
        evaluation.TopBidNotional,
        evaluation.TopAskNotional,
        evaluation.OrderBookImbalance,
        evaluation.Microprice,
        evaluation.AggressiveBuyQuantity,
        evaluation.AggressiveSellQuantity,
        evaluation.AggressiveFlowImbalance,
        evaluation.NormalizedVelocityBps,
        evaluation.RealizedVolatilityBps,
        evaluation.TrendFlowScore,
        evaluation.MarketDataEventTimeUtc,
        evaluation.MarketDataTransactionTimeUtc,
        evaluation.MarketDataLocalReceiptUtc,
        evaluation.EvaluatedAtUtc,
        evaluation.MarketDataAgeMs,
        evaluation.StreamLatencyMs,
        evaluation.IsMarketDataFresh,
        evaluation.Decision,
        evaluation.RejectionReason,
        evaluation.SnapshotJson,
        evaluation.CreatedAt
    };

    private static object ToCounterfactualParam(AdaptiveRollingProfitCounterfactualRecord counterfactual) => new
    {
        counterfactual.PositionId,
        Symbol = (int)counterfactual.Symbol,
        Side = (int)counterfactual.Side,
        counterfactual.EntryPrice,
        counterfactual.Quantity,
        counterfactual.OriginalStopLossPrice,
        counterfactual.OriginalTakeProfitPrice,
        counterfactual.OriginalMaxHoldUntilUtc,
        counterfactual.ActualRollingExitPrice,
        counterfactual.ActualRollingNetPnl,
        counterfactual.ActualRollingClosedAtUtc,
        counterfactual.MaxAdditionalFavorableMove,
        counterfactual.MaxAvoidedAdverseMove,
        counterfactual.CounterfactualExitPrice,
        counterfactual.CounterfactualNetPnl,
        counterfactual.CounterfactualExitReason,
        counterfactual.BetterExitMethod,
        counterfactual.IsActive,
        counterfactual.CompletedAtUtc,
        counterfactual.CreatedAt,
        counterfactual.UpdatedAt
    };
}
