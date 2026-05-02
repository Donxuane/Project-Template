using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AccountInformation;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Every 10 minutes, fetches GET /api/v3/account and compares balances with local positions; logs mismatches.
/// </summary>
public class PositionReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PositionReconciliationWorker> logger) : BackgroundService
{
    private const int DefaultIntervalMinutes = 10;
    private const int RetryDelaySeconds = 30;
    private const decimal DefaultTolerance = 0.00000001m;
    private const int DefaultSnapshotMaxAgeSeconds = 180;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Max(1, configuration.GetValue<int?>("Trading:ReconciliationIntervalMinutes") ?? DefaultIntervalMinutes);
        logger.LogInformation("PositionReconciliationWorker started. Interval: {Interval} min", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PositionReconciliationWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow, RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), stoppingToken);
            }
        }

        logger.LogInformation("PositionReconciliationWorker stopped.");
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();
        var positionRepository = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var balanceRepository = scope.ServiceProvider.GetRequiredService<IBalanceRepository>();
        var reconciliationService = scope.ServiceProvider.GetRequiredService<IPositionReconciliationService>();
        var timeSyncService = scope.ServiceProvider.GetRequiredService<ITimeSyncService>();

        var tradingModeRaw = configuration.GetValue<string?>("Trading:Mode");
        if (!Enum.TryParse(tradingModeRaw, true, out TradingMode tradingMode))
            tradingMode = TradingMode.Spot;

        if (tradingMode != TradingMode.Spot)
        {
            logger.LogInformation(
                "PositionReconciliationWorker skipped because trading mode is not Spot. TradingMode={TradingMode}",
                tradingMode);
            return;
        }

        var tolerance = configuration.GetValue<decimal?>("Trading:ReconciliationQuantityTolerance") ?? DefaultTolerance;
        var maxOpenPositionsPerSymbol = Math.Max(1, configuration.GetValue<int?>("Trading:MaxOpenPositionsPerSymbol") ?? 1);
        var snapshotMaxAgeSeconds = Math.Max(10, configuration.GetValue<int?>("Trading:ReconciliationSnapshotMaxAgeSeconds") ?? DefaultSnapshotMaxAgeSeconds);

        var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync(cancellationToken);

        var accountEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Account.AccoutnInformation);
        var account = await toolService.BinanceClientService.Call<AccountInfoResponse, AccountInfoRequest>(
            new AccountInfoRequest
            {
                RecvWindow = 30000,
                Timestamp = adjustedTimestamp
            }, accountEndpoint, true);

        if (account?.Balances == null)
        {
            logger.LogWarning("PositionReconciliationWorker: no account balances in response");
            return;
        }

        var localOpenPositions = await positionRepository.GetOpenPositionsAsync(cancellationToken);
        var localClosedPositions = await positionRepository.GetClosedPositionsAsync(cancellationToken);
        var latestSnapshots = await balanceRepository.GetLatestForAllAsync(cancellationToken);
        var latestSnapshotsByAsset = latestSnapshots
            .GroupBy(x => x.Asset, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.UpdatedAt == default ? x.CreatedAt : x.UpdatedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        var results = reconciliationService.EvaluateSpot(
            localOpenPositions,
            localClosedPositions,
            account.Balances,
            latestSnapshotsByAsset,
            tolerance,
            maxOpenPositionsPerSymbol,
            TimeSpan.FromSeconds(snapshotMaxAgeSeconds));

        foreach (var result in results)
        {
            if (result.IsMatched)
            {
                logger.LogInformation(
                    "PositionReconciliation matched. Symbol={Symbol} Asset={Asset} LocalOpenQuantity={LocalOpenQuantity} ExchangeFree={ExchangeFree} ExchangeLocked={ExchangeLocked} ExchangeTotal={ExchangeTotal} Difference={Difference} Tolerance={Tolerance} Severity={Severity} Reason={Reason}",
                    result.Symbol,
                    result.Asset,
                    result.LocalOpenQuantity,
                    result.ExchangeFree,
                    result.ExchangeLocked,
                    result.ExchangeTotal,
                    result.Difference,
                    tolerance,
                    result.Severity,
                    result.Reason);
                continue;
            }

            if (string.Equals(result.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError(
                    "PositionReconciliation mismatch. Symbol={Symbol} Asset={Asset} LocalOpenQuantity={LocalOpenQuantity} ExchangeFree={ExchangeFree} ExchangeLocked={ExchangeLocked} ExchangeTotal={ExchangeTotal} Difference={Difference} Tolerance={Tolerance} Severity={Severity} Reason={Reason}",
                    result.Symbol,
                    result.Asset,
                    result.LocalOpenQuantity,
                    result.ExchangeFree,
                    result.ExchangeLocked,
                    result.ExchangeTotal,
                    result.Difference,
                    tolerance,
                    result.Severity,
                    result.Reason);
            }
            else
            {
                logger.LogWarning(
                    "PositionReconciliation mismatch. Symbol={Symbol} Asset={Asset} LocalOpenQuantity={LocalOpenQuantity} ExchangeFree={ExchangeFree} ExchangeLocked={ExchangeLocked} ExchangeTotal={ExchangeTotal} Difference={Difference} Tolerance={Tolerance} Severity={Severity} Reason={Reason}",
                    result.Symbol,
                    result.Asset,
                    result.LocalOpenQuantity,
                    result.ExchangeFree,
                    result.ExchangeLocked,
                    result.ExchangeTotal,
                    result.Difference,
                    tolerance,
                    result.Severity,
                    result.Reason);
            }
        }

        var mismatchCount = results.Count(x => !x.IsMatched);
        var errorCount = results.Count(x => !x.IsMatched && string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        logger.LogInformation(
            "PositionReconciliationWorker completed. TradingMode={TradingMode}, OpenPositions={OpenPositions}, ClosedPositions={ClosedPositions}, Checks={Checks}, Mismatches={MismatchCount}, Errors={ErrorCount}, Tolerance={Tolerance}",
            tradingMode,
            localOpenPositions.Count,
            localClosedPositions.Count,
            results.Count,
            mismatchCount,
            errorCount,
            tolerance);
    }
}
