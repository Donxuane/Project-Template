using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;

namespace TradingBot.Application.DecisionEngine;

public class CandleWarmupService(
    IServiceScopeFactory scopeFactory,
    ILogger<CandleWarmupService> logger) : ICandleWarmupService
{
    private readonly ConcurrentDictionary<TradingSymbol, bool> _readySymbols = new();
    private volatile bool _isWarmedUp;

    public bool IsWarmedUp => _isWarmedUp;

    public async Task WarmUpAsync(IReadOnlyList<TradingSymbol> symbols, CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0)
        {
            _isWarmedUp = true;
            return;
        }

        foreach (var symbol in symbols.Distinct())
        {
            if (_readySymbols.ContainsKey(symbol))
                continue;

            while (!cancellationToken.IsCancellationRequested)
            {
                using var scope = scopeFactory.CreateScope();
                var resolver = scope.ServiceProvider.GetRequiredService<IDataRequirementResolver>();
                var candleService = scope.ServiceProvider.GetRequiredService<ICandleService>();
                var required = resolver.GetRequiredCandles(symbol);
                var current = await candleService.EnsureCandlesAsync(symbol, required, cancellationToken);

                logger.LogInformation(
                    "Warming up {Symbol}: {Current}/{Required} candles",
                    symbol,
                    current,
                    required);

                if (current >= required)
                {
                    _readySymbols[symbol] = true;
                    logger.LogInformation("Warm-up completed for {Symbol}.", symbol);
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        _isWarmedUp = symbols.Distinct().All(symbol => _readySymbols.ContainsKey(symbol));
        if (_isWarmedUp)
            logger.LogInformation("Candle warm-up completed for all symbols.");
    }
}
