using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class BinanceMarketDataProvider(
    ICandleService candleService,
    IDataRequirementResolver dataRequirementResolver,
    ILogger<BinanceMarketDataProvider> logger) : IMarketDataProvider
{
    public async Task<MarketSnapshot?> GetLatestAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var requiredCandles = dataRequirementResolver.GetRequiredCandles(symbol);
            var snapshot = await candleService.GetSnapshotAsync(symbol, requiredCandles, cancellationToken);
            if (snapshot is null)
            {
                logger.LogWarning("DecisionEngine could not build candle snapshot for {Symbol}.", symbol);
                return null;
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DecisionEngine failed to fetch market data for {Symbol}.", symbol);
            return null;
        }
    }
}
