using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models;
using TradingBot.Shared.Shared.Models;

namespace TradingBot.Domain.Interfaces.Services;

public interface IBinanceSettingsService
{
    public T GetPriceFilltersSettings<T>(FilterTypes type);
    public RateLimitterSettings GetRateLimitterSettings(RateLimitType rateLimitType);
}
