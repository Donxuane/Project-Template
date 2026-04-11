using System.Collections.Concurrent;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class PositionManager : IPositionManager
{
    private readonly ConcurrentDictionary<TradingSymbol, SymbolPositionState> _states = new();

    public SymbolPositionState GetState(TradingSymbol symbol)
    {
        return _states.GetOrAdd(symbol, _ => new SymbolPositionState());
    }

    public void Enter(TradingSymbol symbol, PositionType positionType, decimal entryPrice, TrendState trendState, DateTime entryTimeUtc)
    {
        _states.AddOrUpdate(
            symbol,
            _ => new SymbolPositionState
            {
                IsInPosition = true,
                PositionType = positionType,
                EntryPrice = entryPrice,
                EntryTimeUtc = entryTimeUtc,
                TrendState = trendState
            },
            (_, _) => new SymbolPositionState
            {
                IsInPosition = true,
                PositionType = positionType,
                EntryPrice = entryPrice,
                EntryTimeUtc = entryTimeUtc,
                TrendState = trendState
            });
    }

    public void Exit(TradingSymbol symbol, TrendState trendState)
    {
        _states.AddOrUpdate(
            symbol,
            _ => new SymbolPositionState { IsInPosition = false, PositionType = PositionType.None, TrendState = trendState },
            (_, state) => new SymbolPositionState
            {
                IsInPosition = false,
                PositionType = PositionType.None,
                EntryPrice = 0m,
                EntryTimeUtc = DateTime.MinValue,
                TrendState = trendState
            });
    }

    public void UpdateTrend(TradingSymbol symbol, TrendState trendState)
    {
        _states.AddOrUpdate(
            symbol,
            _ => new SymbolPositionState { IsInPosition = false, PositionType = PositionType.None, TrendState = trendState },
            (_, state) => new SymbolPositionState
            {
                IsInPosition = state.IsInPosition,
                PositionType = state.PositionType,
                EntryPrice = state.EntryPrice,
                EntryTimeUtc = state.EntryTimeUtc,
                TrendState = trendState
            });
    }
}
