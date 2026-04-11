using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class SymbolMarketState
{
    public TrendState LastTrendState { get; init; } = TrendState.Neutral;
}
