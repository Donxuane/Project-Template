using System.Reflection;
using TradingBot.Application.DecisionEngine;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class StrategyStaticStateResetter
{
    public static void ResetMovingAverageTrendStrategyState()
    {
        var fields = typeof(MovingAverageTrendStrategy)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>))
            .ToArray();

        foreach (var field in fields)
        {
            var dictionary = field.GetValue(null);
            if (dictionary is null)
                continue;

            var clearMethod = dictionary.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public);
            clearMethod?.Invoke(dictionary, null);
        }
    }

    public static int GetTrackedSymbolCount()
    {
        var field = typeof(MovingAverageTrendStrategy)
            .GetField("LastEntrySignalTimesUtc", BindingFlags.Static | BindingFlags.NonPublic);
        if (field?.GetValue(null) is not System.Collections.Concurrent.ConcurrentDictionary<TradingSymbol, DateTime> map)
            return 0;
        return map.Count;
    }
}
