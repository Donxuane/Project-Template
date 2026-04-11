using System.Collections.Concurrent;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class MarketStateTracker : IMarketStateTracker
{
    private readonly ConcurrentDictionary<TradingSymbol, SymbolMarketState> _states = new();

    public SymbolMarketState GetState(TradingSymbol symbol)
    {
        return _states.GetOrAdd(symbol, _ => new SymbolMarketState());
    }

    public void Update(TradingSymbol symbol, TrendState trendState)
    {
        _states.AddOrUpdate(
            symbol,
            _ => new SymbolMarketState { LastTrendState = trendState },
            (_, _) => new SymbolMarketState { LastTrendState = trendState });
    }
}
