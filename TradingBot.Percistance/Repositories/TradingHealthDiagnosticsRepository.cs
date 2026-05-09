using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Diagnostics;

namespace TradingBot.Percistance.Repositories;

public sealed class TradingHealthDiagnosticsRepository(IDbConnection connection) : ITradingHealthDiagnosticsRepository
{
    public async Task<TradingRuntimeHealthMetrics> CollectMetricsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH active_close_orders AS (
                SELECT o.id, o.parent_position_id
                FROM orders o
                WHERE o.parent_position_id IS NOT NULL
                  AND o.side = @SellSide
                  AND o.close_reason <> @CloseReasonNone
                  AND o.status = ANY(@ActiveStatuses)
                  AND o.processing_status <> ALL(@InactiveProcessingStatuses)
            ),
            latest_bnb AS (
                SELECT COALESCE(updated_at, created_at) AS latest_at
                FROM balance_snapshots
                WHERE asset = 'BNB'
                ORDER BY COALESCE(updated_at, created_at) DESC
                LIMIT 1
            ),
            latest_usdt AS (
                SELECT COALESCE(updated_at, created_at) AS latest_at
                FROM balance_snapshots
                WHERE asset = 'USDT'
                ORDER BY COALESCE(updated_at, created_at) DESC
                LIMIT 1
            )
            SELECT
                EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'positions'
                      AND column_name = 'is_closing'
                ) AS HasPositionsIsClosingColumn,
                to_regclass('public.balance_snapshot_history') IS NOT NULL AS HasBalanceSnapshotHistoryTable,

                (SELECT COUNT(*) FROM positions WHERE is_open = false AND quantity <> 0) AS ClosedPositionsWithNonZeroQuantity,
                (SELECT COUNT(*) FROM positions WHERE is_open = false AND is_closing = true) AS ClosedPositionsWithIsClosingTrue,
                (SELECT COUNT(*) FROM positions WHERE is_open = true AND quantity <= 0) AS OpenPositionsWithNonPositiveQuantity,
                (SELECT COUNT(*) FROM positions WHERE is_open = true AND average_price <= 0) AS OpenPositionsWithMissingAveragePrice,
                (SELECT COUNT(*) FROM positions WHERE is_open = false AND closed_at IS NULL) AS ClosedPositionsMissingClosedAt,
                (SELECT COUNT(*) FROM positions WHERE is_open = false AND exit_price IS NULL) AS ClosedPositionsMissingExitPrice,

                (
                    SELECT COUNT(DISTINCT o.id)
                    FROM orders o
                    JOIN trade_executions te ON te.order_id = o.id
                    WHERE o.processing_status = @PositionUpdated
                      AND te.position_processed_at IS NULL
                ) AS PositionUpdatedOrdersWithUnprocessedExecutions,
                (
                    SELECT COUNT(DISTINCT o.id)
                    FROM orders o
                    JOIN trade_executions te ON te.order_id = o.id
                    WHERE o.processing_status = @TradesSynced
                ) AS TradeSyncedOrdersWithExecutions,
                (
                    SELECT COUNT(*)
                    FROM orders o
                    WHERE o.processing_status = @PositionUpdating
                      AND o.updated_at < now() - interval '5 minutes'
                ) AS PositionUpdatingOrdersStuck,
                (
                    SELECT COUNT(*)
                    FROM orders o
                    WHERE o.status = @FilledStatus
                      AND NOT EXISTS (
                          SELECT 1
                          FROM trade_executions te
                          WHERE te.order_id = o.id
                      )
                ) AS FilledOrdersWithoutExecutions,
                (
                    SELECT COUNT(*)
                    FROM trade_executions te
                    LEFT JOIN orders o ON o.id = te.order_id
                    WHERE o.id IS NULL
                ) AS TradeExecutionsWithoutMatchingOrders,

                (
                    SELECT COUNT(*)
                    FROM (
                        SELECT parent_position_id
                        FROM active_close_orders
                        GROUP BY parent_position_id
                        HAVING COUNT(*) > 1
                    ) duplicates
                ) AS ParentPositionsWithMultipleActiveCloseOrders,
                (
                    SELECT COUNT(*)
                    FROM positions p
                    WHERE p.is_closing = true
                      AND NOT EXISTS (
                          SELECT 1
                          FROM active_close_orders a
                          WHERE a.parent_position_id = p.id
                      )
                ) AS ClosingPositionsWithoutActiveCloseOrder,
                (
                    SELECT COUNT(*)
                    FROM positions p
                    WHERE p.is_open = true
                      AND p.is_closing = false
                      AND EXISTS (
                          SELECT 1
                          FROM active_close_orders a
                          WHERE a.parent_position_id = p.id
                      )
                ) AS OpenPositionsWithActiveCloseOrderButNotClosing,

                (SELECT latest_at FROM latest_bnb) AS LatestBnbAt,
                (SELECT latest_at FROM latest_usdt) AS LatestUsdtAt,
                CASE
                    WHEN to_regclass('public.balance_snapshot_history') IS NULL THEN true
                    ELSE NOT EXISTS (SELECT 1 FROM public.balance_snapshot_history LIMIT 1)
                END AS BalanceSnapshotHistoryEmpty;
            """;

        return await connection.QuerySingleAsync<TradingRuntimeHealthMetrics>(
            new CommandDefinition(
                sql,
                new
                {
                    SellSide = (int)OrderSide.SELL,
                    CloseReasonNone = (int)CloseReason.None,
                    ActiveStatuses = new[]
                    {
                        (int)OrderStatuses.NEW,
                        (int)OrderStatuses.PARTIALLY_FILLED,
                        (int)OrderStatuses.FILLED
                    },
                    InactiveProcessingStatuses = new[]
                    {
                        (int)ProcessingStatus.PositionUpdated,
                        (int)ProcessingStatus.PositionUpdateFailed,
                        (int)ProcessingStatus.Completed
                    },
                    PositionUpdated = (int)ProcessingStatus.PositionUpdated,
                    TradesSynced = (int)ProcessingStatus.TradesSynced,
                    PositionUpdating = (int)ProcessingStatus.PositionUpdating,
                    FilledStatus = (int)OrderStatuses.FILLED
                },
                cancellationToken: cancellationToken));
    }
}
