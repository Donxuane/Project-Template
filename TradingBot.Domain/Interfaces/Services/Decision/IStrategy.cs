using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IStrategy
{
    int RequiredPeriods { get; }
    Task<StrategySignalResult> GenerateSignalAsync(MarketSnapshot marketData, CancellationToken cancellationToken = default);
}
