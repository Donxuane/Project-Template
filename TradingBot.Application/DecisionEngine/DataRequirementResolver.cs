using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;

namespace TradingBot.Application.DecisionEngine;

public class DataRequirementResolver(
    IConfiguration configuration,
    IEnumerable<IStrategy> strategies,
    IAtrService atrService,
    IVolatilityService volatilityService,
    IMarketConditionService marketConditionService) : IDataRequirementResolver
{
    private const string ConfigSection = "DecisionEngine:Candles";

    public int SafetyBuffer { get; } = GetInt(configuration, $"{ConfigSection}:SafetyBuffer", 20, 0);

    public int GetBaseRequiredCandles(TradingSymbol symbol)
    {
        var requirements = new List<int>
        {
            atrService.RequiredPeriods,
            volatilityService.RequiredPeriods,
            marketConditionService.RequiredPeriods
        };

        requirements.AddRange(strategies.Select(s => s.RequiredPeriods));
        return requirements.Count == 0 ? 0 : requirements.Max();
    }

    public int GetRequiredCandles(TradingSymbol symbol)
    {
        var baseRequired = GetBaseRequiredCandles(symbol);
        return baseRequired + SafetyBuffer;
    }

    private static int GetInt(IConfiguration configuration, string key, int defaultValue, int minValue)
    {
        var raw = configuration[key];
        if (!int.TryParse(raw, out var value))
            return defaultValue;

        return Math.Max(value, minValue);
    }
}
