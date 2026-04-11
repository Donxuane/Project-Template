using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface ITrendStateService
{
    int RequiredPeriods { get; }
    TrendAnalysisResult Analyze(MarketSnapshot marketData, int shortPeriod, int longPeriod);
}
