using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Resolves evaluation time from confirmed closed 1m candles only (excludes the in-progress minute).
/// </summary>
public static class ConfirmedClosedEvalUtcResolver
{
    public static DateTime Resolve(
        IReadOnlyDictionary<TradingSymbol, SymbolValidationResult> validatedDataBySymbol,
        DateTime nowUtc)
    {
        DateTime? confirmed = null;
        foreach (var data in validatedDataBySymbol.Values)
        {
            if (data.Candles.Count == 0)
                continue;

            var symbolConfirmed = ResolveSymbolConfirmedCloseUtc(data.Candles, nowUtc);
            confirmed = confirmed.HasValue
                ? (symbolConfirmed < confirmed.Value ? symbolConfirmed : confirmed.Value)
                : symbolConfirmed;
        }

        return confirmed ?? nowUtc;
    }

    public static DateTime ResolveSymbolConfirmedCloseUtc(
        IReadOnlyList<KlineCandle> candles,
        DateTime nowUtc)
    {
        if (candles.Count == 0)
            return nowUtc;

        var idx = candles.Count - 1;
        while (idx > 0 && candles[idx].OpenTimeUtc.AddMinutes(1) > nowUtc)
            idx--;

        return candles[idx].OpenTimeUtc.AddMinutes(1);
    }
}
