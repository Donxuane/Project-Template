using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Fetches ticker prices every 5 seconds and caches them in Redis.
/// </summary>
public class MarketDataWorker(IServiceScopeFactory scopeFactory, ILogger<MarketDataWorker> logger) : BackgroundService
{
    private const int IntervalSeconds = 5;
    private const int RetryDelaySeconds = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MarketDataWorker started. Interval: {Interval}s", IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FetchAndCachePricesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MarketDataWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow, RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), stoppingToken);
            }
        }

        logger.LogInformation("MarketDataWorker stopped.");
    }

    private async Task FetchAndCachePricesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();
        var priceCacheService = scope.ServiceProvider.GetRequiredService<IPriceCacheService>();

        var endpoint = toolService.BinanceEndpointsService.GetEndpoint(MarketData.SymbolPriceTicker);
        var symbols = Enum.GetValues<TradingSymbol>();



        try
        {
            var request = new SymbolPriceTickerRequest { Symbols = symbols.Select(x => x.ToString()).ToList() };
            var response = await toolService.BinanceClientService.Call<List<SymbolPriceTickerResponse>, SymbolPriceTickerRequest>(
                request, endpoint, false);

            foreach (var symbol in response)
            {
                if (Enum.TryParse<TradingSymbol>(symbol.Symbol, out var symbolToStore))
                {
                    if (symbol?.Price != null && decimal.TryParse(symbol.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    {
                        await priceCacheService.SetCachedPriceAsync(symbolToStore, price, cancellationToken);
                        logger.LogDebug("MarketDataWorker cached price {Symbol}={Price}", symbol, price);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MarketDataWorker failed to fetch price for {Symbol}", symbols);
        }

    }
}
