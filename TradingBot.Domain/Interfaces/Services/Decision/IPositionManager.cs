using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IPositionManager
{
    SymbolPositionState GetState(TradingSymbol symbol);
    void Enter(TradingSymbol symbol, PositionType positionType, decimal entryPrice, TrendState trendState, DateTime entryTimeUtc);
    void Exit(TradingSymbol symbol, TrendState trendState);
    void UpdateTrend(TradingSymbol symbol, TrendState trendState);
}
