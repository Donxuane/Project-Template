using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IDataRequirementResolver
{
    int SafetyBuffer { get; }
    int GetBaseRequiredCandles(TradingSymbol symbol);
    int GetRequiredCandles(TradingSymbol symbol);
}
