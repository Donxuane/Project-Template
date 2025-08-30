using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models;

namespace TradingBot.Domain.Interfaces.ExternalServices;

public interface IBinanceSettingsService
{
    public Task<(decimal? price, decimal? qty)> ValidatePrice(TradingSymbol symbol, decimal price);
    public Task<List<RateLimit>?>? GetRateLimitterSettings(RateLimitType type, TradingSymbol symbol);
}
