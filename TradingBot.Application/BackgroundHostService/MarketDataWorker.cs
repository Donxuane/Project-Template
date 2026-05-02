using System.Globalization;
using Microsoft.Extensions.Configuration;
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
public class MarketDataWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MarketDataWorker> logger) : BackgroundService
{
    private const int DefaultIntervalSeconds = 5;
    private const int DefaultMaxConcurrency = 3;
    private const int RetryDelaySeconds = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Max(1, configuration.GetValue<int?>("Workers:MarketData:IntervalSeconds") ?? DefaultIntervalSeconds);
        logger.LogInformation("MarketDataWorker started. Interval: {Interval}s", intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FetchAndCachePricesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
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
        var maxConcurrency = Math.Max(1, configuration.GetValue<int?>("MarketData:MaxConcurrency") ?? DefaultMaxConcurrency);



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

            var responseSymbols = response
                .Select(x => Enum.TryParse<TradingSymbol>(x.Symbol, out var parsed) ? parsed : (TradingSymbol?)null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToHashSet();
            var missingSymbols = symbols.Where(x => !responseSymbols.Contains(x)).ToList();
            if (missingSymbols.Count > 0)
            {
                logger.LogWarning(
                    "MarketDataWorker batch ticker response missing symbols. MissingCount={MissingCount}, Symbols={Symbols}",
                    missingSymbols.Count,
                    string.Join(",", missingSymbols));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MarketDataWorker batch ticker request failed. Falling back to per-symbol requests.");
            await FetchPerSymbolAsync(toolService, priceCacheService, endpoint, symbols, maxConcurrency, cancellationToken);
        }

    }

    private async Task FetchPerSymbolAsync(
        IToolService toolService,
        IPriceCacheService priceCacheService,
        Shared.Shared.Models.Endpoint endpoint,
        IReadOnlyList<TradingSymbol> symbols,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = symbols.Select(async symbol =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var request = new SymbolPriceTickerRequest { Symbol = symbol.ToString() };
                var response = await toolService.BinanceClientService.Call<SymbolPriceTickerResponse, SymbolPriceTickerRequest>(
                    request,
                    endpoint,
                    false);

                if (!decimal.TryParse(response.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                {
                    logger.LogWarning(
                        "MarketDataWorker per-symbol parse failed. Symbol={Symbol}, RawPrice={RawPrice}",
                        symbol,
                        response.Price);
                    return;
                }

                await priceCacheService.SetCachedPriceAsync(symbol, price, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MarketDataWorker per-symbol fetch failed. Symbol={Symbol}", symbol);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }
}
