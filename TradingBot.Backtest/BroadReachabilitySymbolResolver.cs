using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class BroadReachabilitySymbolResolver
{
    private static readonly TradingSymbol[] DefaultSymbols =
    [
        TradingSymbol.ETHUSDT,
        TradingSymbol.BNBUSDT,
        TradingSymbol.SOLUSDT,
        TradingSymbol.BTCUSDT
    ];

    public static IReadOnlyList<TradingSymbol> ResolveAvailableSymbols(BacktestSettings settings)
    {
        var dataDir = settings.DataDirectory;
        var resolved = new List<TradingSymbol>();
        foreach (var symbol in DefaultSymbols)
        {
            if (HasLocalData(dataDir, symbol))
                resolved.Add(symbol);
        }

        return resolved;
    }

    private static bool HasLocalData(string dataDir, TradingSymbol symbol)
    {
        var symbolText = symbol.ToString();
        return File.Exists(Path.Combine(dataDir, $"{symbolText}-1m.json"))
               || File.Exists(Path.Combine(dataDir, $"{symbolText}-1m.csv"))
               || File.Exists(Path.Combine(dataDir, $"{symbolText}.json"))
               || File.Exists(Path.Combine(dataDir, $"{symbolText}.csv"));
    }
}
