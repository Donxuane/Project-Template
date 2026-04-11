using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface ICandleService
{
    int GetBufferedCount(TradingSymbol symbol);
    Task<int> EnsureCandlesAsync(TradingSymbol symbol, int requiredCandles, CancellationToken cancellationToken = default);
    Task<MarketSnapshot?> GetSnapshotAsync(TradingSymbol symbol, int requiredCandles, CancellationToken cancellationToken = default);
}
