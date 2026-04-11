using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IMarketConditionService
{
    int RequiredPeriods { get; }
    MarketConditionResult Evaluate(MarketSnapshot snapshot);
}
