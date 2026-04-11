using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IMarketStateTracker
{
    SymbolMarketState GetState(TradingSymbol symbol);
    void Update(TradingSymbol symbol, TrendState trendState);
}
