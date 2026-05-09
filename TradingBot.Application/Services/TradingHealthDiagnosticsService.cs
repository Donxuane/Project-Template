using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Diagnostics;

namespace TradingBot.Application.Services;

public sealed class TradingHealthDiagnosticsService(
    ITradingHealthDiagnosticsRepository diagnosticsRepository,
    ILogger<TradingHealthDiagnosticsService> logger) : ITradingHealthDiagnosticsService
{
    private static readonly TimeSpan DefaultBalanceMaxAge = TimeSpan.FromMinutes(10);

    public async Task<TradingRuntimeHealthResult> RunAsync(TimeSpan? maxBalanceAge = null, CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTime.UtcNow;
        var effectiveMaxAge = NormalizeMaxAge(maxBalanceAge);
        var metrics = await diagnosticsRepository.CollectMetricsAsync(cancellationToken);

        var criticalIssues = new List<string>();
        var warnings = new List<string>();
        var counts = new TradingRuntimeHealthIssueCounts();

        AddSchemaIssues(metrics, criticalIssues, counts);
        AddPositionIssues(metrics, criticalIssues, counts);
        AddLifecycleIssues(metrics, criticalIssues, counts);
        AddCloseSafetyIssues(metrics, criticalIssues, warnings, counts);
        AddBalanceIssues(metrics, checkedAt, effectiveMaxAge, warnings, counts);

        counts.Critical = criticalIssues.Count;
        counts.Warnings = warnings.Count;

        var result = new TradingRuntimeHealthResult
        {
            CheckedAt = checkedAt,
            CriticalIssues = criticalIssues,
            Warnings = warnings,
            IsHealthy = criticalIssues.Count == 0,
            Counts = counts
        };

        if (!result.IsHealthy)
        {
            logger.LogError(
                "Trading DB health check failed at {CheckedAt}. Critical={CriticalCount}, Warnings={WarningCount}",
                checkedAt,
                result.CriticalIssues.Count,
                result.Warnings.Count);

            foreach (var issue in result.CriticalIssues)
            {
                logger.LogError("Trading DB health critical: {Issue}", issue);
            }
        }

        foreach (var warning in result.Warnings)
        {
            logger.LogWarning("Trading DB health warning: {Warning}", warning);
        }

        if (result.IsHealthy && result.Warnings.Count == 0)
        {
            logger.LogInformation("Trading DB health check healthy at {CheckedAt}.", checkedAt);
        }
        else if (result.IsHealthy)
        {
            logger.LogInformation(
                "Trading DB health check healthy-with-warnings at {CheckedAt}. Warnings={WarningCount}",
                checkedAt,
                result.Warnings.Count);
        }

        return result;
    }

    private static TimeSpan NormalizeMaxAge(TimeSpan? maxBalanceAge)
    {
        if (!maxBalanceAge.HasValue || maxBalanceAge.Value <= TimeSpan.Zero)
            return DefaultBalanceMaxAge;
        return maxBalanceAge.Value;
    }

    private static void AddSchemaIssues(
        TradingRuntimeHealthMetrics metrics,
        List<string> criticalIssues,
        TradingRuntimeHealthIssueCounts counts)
    {
        if (!metrics.HasPositionsIsClosingColumn)
        {
            criticalIssues.Add("Schema missing required column: positions.is_closing");
            counts.SchemaCritical++;
        }

        if (!metrics.HasBalanceSnapshotHistoryTable)
        {
            criticalIssues.Add("Schema missing required table: balance_snapshot_history");
            counts.SchemaCritical++;
        }
    }

    private static void AddPositionIssues(
        TradingRuntimeHealthMetrics metrics,
        List<string> criticalIssues,
        TradingRuntimeHealthIssueCounts counts)
    {
        AddCriticalIf(metrics.ClosedPositionsWithNonZeroQuantity, "Closed positions with quantity <> 0", criticalIssues, counts, c => c.PositionCritical++);
        AddCriticalIf(metrics.ClosedPositionsWithIsClosingTrue, "Closed positions with is_closing = true", criticalIssues, counts, c => c.PositionCritical++);
        AddCriticalIf(metrics.OpenPositionsWithNonPositiveQuantity, "Open positions with quantity <= 0", criticalIssues, counts, c => c.PositionCritical++);
        AddCriticalIf(metrics.OpenPositionsWithMissingAveragePrice, "Open positions with missing/invalid average_price", criticalIssues, counts, c => c.PositionCritical++);
        AddCriticalIf(metrics.ClosedPositionsMissingClosedAt, "Closed positions missing closed_at", criticalIssues, counts, c => c.PositionCritical++);
        AddCriticalIf(metrics.ClosedPositionsMissingExitPrice, "Closed positions missing exit_price", criticalIssues, counts, c => c.PositionCritical++);
    }

    private static void AddLifecycleIssues(
        TradingRuntimeHealthMetrics metrics,
        List<string> criticalIssues,
        TradingRuntimeHealthIssueCounts counts)
    {
        AddCriticalIf(metrics.PositionUpdatedOrdersWithUnprocessedExecutions, "Orders PositionUpdated but trade_executions.position_processed_at is null", criticalIssues, counts, c => c.LifecycleCritical++);
        AddCriticalIf(metrics.TradeSyncedOrdersWithExecutions, "Orders stuck at TradesSynced while executions exist", criticalIssues, counts, c => c.LifecycleCritical++);
        AddCriticalIf(metrics.PositionUpdatingOrdersStuck, "Orders stuck at PositionUpdating for more than 5 minutes", criticalIssues, counts, c => c.LifecycleCritical++);
        AddCriticalIf(metrics.FilledOrdersWithoutExecutions, "Filled orders with no trade_executions", criticalIssues, counts, c => c.LifecycleCritical++);
        AddCriticalIf(metrics.TradeExecutionsWithoutMatchingOrders, "trade_executions without matching orders", criticalIssues, counts, c => c.LifecycleCritical++);
    }

    private static void AddCloseSafetyIssues(
        TradingRuntimeHealthMetrics metrics,
        List<string> criticalIssues,
        List<string> warnings,
        TradingRuntimeHealthIssueCounts counts)
    {
        AddCriticalIf(metrics.ParentPositionsWithMultipleActiveCloseOrders, "More than one active close order for same parent_position_id", criticalIssues, counts, c => c.CloseSafetyCritical++);
        AddWarningIf(metrics.ClosingPositionsWithoutActiveCloseOrder, "Positions with is_closing = true but no active close order exists", warnings, counts, c => c.CloseSafetyWarnings++);
        AddCriticalIf(metrics.OpenPositionsWithActiveCloseOrderButNotClosing, "Open positions with active close order but is_closing = false", criticalIssues, counts, c => c.CloseSafetyCritical++);
    }

    private static void AddBalanceIssues(
        TradingRuntimeHealthMetrics metrics,
        DateTime checkedAt,
        TimeSpan maxBalanceAge,
        List<string> warnings,
        TradingRuntimeHealthIssueCounts counts)
    {
        if (!metrics.LatestBnbAt.HasValue)
        {
            warnings.Add("Missing latest BNB balance row in balance_snapshots.");
            counts.BalanceWarnings++;
        }
        else if (checkedAt - metrics.LatestBnbAt.Value > maxBalanceAge)
        {
            warnings.Add($"BNB balance snapshot is stale. LastUpdatedUtc={metrics.LatestBnbAt.Value:o}.");
            counts.BalanceWarnings++;
        }

        if (!metrics.LatestUsdtAt.HasValue)
        {
            warnings.Add("Missing latest USDT balance row in balance_snapshots.");
            counts.BalanceWarnings++;
        }
        else if (checkedAt - metrics.LatestUsdtAt.Value > maxBalanceAge)
        {
            warnings.Add($"USDT balance snapshot is stale. LastUpdatedUtc={metrics.LatestUsdtAt.Value:o}.");
            counts.BalanceWarnings++;
        }

        if (metrics.BalanceSnapshotHistoryEmpty)
        {
            warnings.Add("balance_snapshot_history is empty.");
            counts.BalanceWarnings++;
        }
    }

    private static void AddCriticalIf(
        int count,
        string label,
        List<string> criticalIssues,
        TradingRuntimeHealthIssueCounts counters,
        Action<TradingRuntimeHealthIssueCounts> incrementCategory)
    {
        if (count <= 0)
            return;

        criticalIssues.Add($"{label}. Count={count}");
        incrementCategory(counters);
    }

    private static void AddWarningIf(
        int count,
        string label,
        List<string> warnings,
        TradingRuntimeHealthIssueCounts counts,
        Action<TradingRuntimeHealthIssueCounts> incrementCategory)
    {
        if (count <= 0)
            return;

        warnings.Add($"{label}. Count={count}");
        incrementCategory(counts);
    }
}
