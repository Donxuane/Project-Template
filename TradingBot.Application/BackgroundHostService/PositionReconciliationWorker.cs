using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AccountInformation;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Every 10 minutes, fetches GET /api/v3/account and compares balances with local positions; logs mismatches.
/// </summary>
public class PositionReconciliationWorker(IServiceScopeFactory scopeFactory, ILogger<PositionReconciliationWorker> logger) : BackgroundService
{
    private const int IntervalMinutes = 10;
    private const int RetryDelaySeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PositionReconciliationWorker started. Interval: {Interval} min", IntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(IntervalMinutes), stoppingToken);
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
        var timeSyncService = scope.ServiceProvider.GetRequiredService<ITimeSyncService>();

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

        var localPositions = await positionRepository.GetOpenPositionsAsync(cancellationToken);
        var balanceByAsset = account.Balances.ToDictionary(
            b => b.Asset,
            b =>
            {
                var free = decimal.TryParse(b.Free, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 0m;
                var locked = decimal.TryParse(b.Locked, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : 0m;
                return free + locked;
            },
            StringComparer.OrdinalIgnoreCase);

        foreach (var position in localPositions)
        {
            var baseAsset = position.Symbol.ToString().Replace("USDT", "", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(baseAsset))
                continue;

            if (!balanceByAsset.TryGetValue(baseAsset, out var exchangeBalance))
            {
                logger.LogWarning(
                    "PositionReconciliationWorker: mismatch Symbol={Symbol} BaseAsset={BaseAsset} LocalQuantity={LocalQty} Exchange balance not found",
                    position.Symbol, baseAsset, position.Quantity);
                continue;
            }

            var localQty = position.Quantity;
            if (Math.Abs(localQty - exchangeBalance) > 0.00000001m)
            {
                logger.LogWarning(
                    "PositionReconciliationWorker: mismatch Symbol={Symbol} BaseAsset={BaseAsset} LocalQuantity={LocalQty} ExchangeBalance={ExchangeBalance}",
                    position.Symbol, baseAsset, localQty, exchangeBalance);
            }
        }

        logger.LogDebug("PositionReconciliationWorker: reconciled {Count} positions", localPositions.Count);
    }
}
