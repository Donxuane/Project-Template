using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Shared.Shared.Models;

namespace TradingBot.Percistance.Services;

public class BinanceSettingsService(IConfiguration configuration) : IBinanceSettingsService
{
    public T GetPriceFilltersSettings<T>(FilterTypes type)
    {
        var data = configuration.GetSection($"PriceValidators:{type}").Get<T>();
        return data == null ? throw new Exception("Settings Not Found") : data;
    }

    public RateLimitterSettings GetRateLimitterSettings(RateLimitType rateLimitType)
    {
        var data = configuration.GetSection($"RateLimiterSettings:{rateLimitType}").Get<RateLimitterSettings>();
        return data ?? throw new Exception("Settings Not Found"); 
    }
}
