using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface ITrendStateService
{
    int GetRequiredPeriods(int shortPeriod, int longPeriod);
    TrendAnalysisResult Analyze(MarketSnapshot marketData, int shortPeriod, int longPeriod);
}
