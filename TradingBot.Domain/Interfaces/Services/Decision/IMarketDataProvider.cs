using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IMarketDataProvider
{
    Task<MarketSnapshot?> GetLatestAsync(TradingSymbol symbol, CancellationToken cancellationToken = default);
}
