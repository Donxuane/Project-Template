using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Syncs account balances from Binance (GET /api/v3/account) into balance_snapshots table.
/// </summary>
public class BalanceSyncWorker(IServiceScopeFactory scopeFactory, ILogger<BalanceSyncWorker> logger) : BackgroundService
{
    private const int IntervalSeconds = 60;
    private const int RetryDelaySeconds = 15;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BalanceSyncWorker started. Interval: {Interval}s", IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncBalancesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BalanceSyncWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow, RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), stoppingToken);
            }
        }

        logger.LogInformation("BalanceSyncWorker stopped.");
    }

    private async Task SyncBalancesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();
        var balanceRepository = scope.ServiceProvider.GetRequiredService<IBalanceRepository>();
        var timeSyncService = scope.ServiceProvider.GetRequiredService<ITimeSyncService>();

        var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync(cancellationToken);

        var accountEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Account.AccoutnInformation);
        var response = await toolService.BinanceClientService.Call<AccountInfoResponse, AccountInfoRequest>(
            new AccountInfoRequest
            {
                OmitZeroBalances = false,
                RecvWindow = 30000,
                Timestamp = adjustedTimestamp
            }, accountEndpoint, true);

        if (response?.Balances == null || response.Balances.Count == 0)
        {
            logger.LogDebug("BalanceSyncWorker: no balances in account response");
            return;
        }


        var balance = response.Balances.Select(x => new BalanceSnapshot
        {
            Asset = x.Asset,
            Symbol = Enum.TryParse<Assets>(x.Asset, out var result) ? result : default,
            Side = OrderSide.BUY,
            Free = decimal.TryParse(x.Free, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 0m,
            Locked = decimal.TryParse(x.Locked, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : 0m
        }).ToList();
        await balanceRepository.UpsertLatestAsync(balance, cancellationToken);

        logger.LogInformation("BalanceSyncWorker saved {Count} balance snapshots at {Time}", balance.Count, DateTime.UtcNow);
    }
}
