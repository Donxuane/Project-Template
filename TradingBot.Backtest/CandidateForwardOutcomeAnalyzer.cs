using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record ForwardOutcomeAnalytics
{
    public decimal? ForwardMfe15Percent { get; init; }
    public decimal? ForwardMfe30Percent { get; init; }
    public decimal? ForwardMfe60Percent { get; init; }
    public decimal? ForwardMae15Percent { get; init; }
    public decimal? ForwardMae30Percent { get; init; }
    public decimal? ForwardMae60Percent { get; init; }
    public decimal? Lock90DistancePercent { get; init; }
    public decimal? Lock95DistancePercent { get; init; }
    public decimal? Lock98DistancePercent { get; init; }
    public bool Lock90ReachableWithin60m { get; init; }
    public bool Lock95ReachableWithin60m { get; init; }
    public bool Lock98ReachableWithin60m { get; init; }
    public int? TimeToLock90Minutes { get; init; }
    public int? TimeToLock95Minutes { get; init; }
    public int? TimeToLock98Minutes { get; init; }
}

public static class CandidateForwardOutcomeAnalyzer
{
    public static ForwardOutcomeAnalytics Analyze(
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        decimal? expectedMovePercent,
        int forwardHorizonMinutes = 60)
    {
        if (oneMinuteCandles.Count == 0 || entryPrice <= 0m)
            return Empty(expectedMovePercent);

        var entryIdx = FindEntryIndex(oneMinuteCandles, entryTimeUtc);
        if (entryIdx < 0)
            return Empty(expectedMovePercent);

        var lock90 = ComputeLockDistance(expectedMovePercent, 90m);
        var lock95 = ComputeLockDistance(expectedMovePercent, 95m);
        var lock98 = ComputeLockDistance(expectedMovePercent, 98m);

        var mfe15 = ComputeForwardMfe(oneMinuteCandles, entryIdx, entryPrice, 15);
        var mfe30 = ComputeForwardMfe(oneMinuteCandles, entryIdx, entryPrice, 30);
        var mfe60 = ComputeForwardMfe(oneMinuteCandles, entryIdx, entryPrice, forwardHorizonMinutes);
        var mae15 = ComputeForwardMae(oneMinuteCandles, entryIdx, entryPrice, 15);
        var mae30 = ComputeForwardMae(oneMinuteCandles, entryIdx, entryPrice, 30);
        var mae60 = ComputeForwardMae(oneMinuteCandles, entryIdx, entryPrice, forwardHorizonMinutes);

        var timeToLock90 = ComputeTimeToLockMinutes(oneMinuteCandles, entryIdx, entryPrice, lock90, forwardHorizonMinutes);
        var timeToLock95 = ComputeTimeToLockMinutes(oneMinuteCandles, entryIdx, entryPrice, lock95, forwardHorizonMinutes);
        var timeToLock98 = ComputeTimeToLockMinutes(oneMinuteCandles, entryIdx, entryPrice, lock98, forwardHorizonMinutes);

        return new ForwardOutcomeAnalytics
        {
            ForwardMfe15Percent = mfe15,
            ForwardMfe30Percent = mfe30,
            ForwardMfe60Percent = mfe60,
            ForwardMae15Percent = mae15,
            ForwardMae30Percent = mae30,
            ForwardMae60Percent = mae60,
            Lock90DistancePercent = lock90,
            Lock95DistancePercent = lock95,
            Lock98DistancePercent = lock98,
            Lock90ReachableWithin60m = timeToLock90.HasValue,
            Lock95ReachableWithin60m = timeToLock95.HasValue,
            Lock98ReachableWithin60m = timeToLock98.HasValue,
            TimeToLock90Minutes = timeToLock90,
            TimeToLock95Minutes = timeToLock95,
            TimeToLock98Minutes = timeToLock98
        };
    }

    public static decimal? ComputeLockDistance(decimal? expectedMovePercent, decimal lockThresholdPercent)
    {
        if (!expectedMovePercent.HasValue || expectedMovePercent.Value <= 0m)
            return null;
        return Math.Round(expectedMovePercent.Value * lockThresholdPercent / 100m, 6);
    }

    public static decimal? ComputeRewardRisk(decimal? expectedMovePercent, decimal? distanceToInvalidationPercent)
    {
        if (!expectedMovePercent.HasValue || !distanceToInvalidationPercent.HasValue || distanceToInvalidationPercent.Value <= 0m)
            return null;
        return Math.Round(expectedMovePercent.Value / distanceToInvalidationPercent.Value, 6);
    }

    public static decimal? ComputeForwardMfePercent(
        IReadOnlyList<KlineCandle> candles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        int horizonMinutes)
    {
        if (candles.Count == 0 || entryPrice <= 0m)
            return null;
        var entryIdx = FindEntryIndex(candles, entryTimeUtc);
        return entryIdx < 0 ? null : ComputeForwardMfe(candles, entryIdx, entryPrice, horizonMinutes);
    }

    public static decimal? ComputeForwardMaePercent(
        IReadOnlyList<KlineCandle> candles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        int horizonMinutes)
    {
        if (candles.Count == 0 || entryPrice <= 0m)
            return null;
        var entryIdx = FindEntryIndex(candles, entryTimeUtc);
        return entryIdx < 0 ? null : ComputeForwardMae(candles, entryIdx, entryPrice, horizonMinutes);
    }

    private static ForwardOutcomeAnalytics Empty(decimal? expectedMovePercent)
    {
        return new ForwardOutcomeAnalytics
        {
            Lock90DistancePercent = ComputeLockDistance(expectedMovePercent, 90m),
            Lock95DistancePercent = ComputeLockDistance(expectedMovePercent, 95m),
            Lock98DistancePercent = ComputeLockDistance(expectedMovePercent, 98m)
        };
    }

    private static int FindEntryIndex(IReadOnlyList<KlineCandle> candles, DateTime entryTimeUtc)
    {
        for (var i = candles.Count - 1; i >= 0; i--)
        {
            if (candles[i].OpenTimeUtc <= entryTimeUtc)
                return i;
        }

        return -1;
    }

    private static decimal? ComputeForwardMfe(
        IReadOnlyList<KlineCandle> candles,
        int entryIdx,
        decimal entryPrice,
        int minutes)
    {
        var maxHigh = entryPrice;
        var end = candles[entryIdx].OpenTimeUtc.AddMinutes(minutes);
        for (var i = entryIdx; i < candles.Count; i++)
        {
            if (candles[i].OpenTimeUtc > end)
                break;
            if (candles[i].High > maxHigh)
                maxHigh = candles[i].High;
        }

        return Math.Round((maxHigh - entryPrice) / entryPrice * 100m, 6);
    }

    private static decimal? ComputeForwardMae(
        IReadOnlyList<KlineCandle> candles,
        int entryIdx,
        decimal entryPrice,
        int minutes)
    {
        var minLow = entryPrice;
        var end = candles[entryIdx].OpenTimeUtc.AddMinutes(minutes);
        for (var i = entryIdx; i < candles.Count; i++)
        {
            if (candles[i].OpenTimeUtc > end)
                break;
            if (candles[i].Low < minLow)
                minLow = candles[i].Low;
        }

        return Math.Round((minLow - entryPrice) / entryPrice * 100m, 6);
    }

    private static int? ComputeTimeToLockMinutes(
        IReadOnlyList<KlineCandle> candles,
        int entryIdx,
        decimal entryPrice,
        decimal? lockDistancePercent,
        int horizonMinutes)
    {
        if (!lockDistancePercent.HasValue)
            return null;

        var targetPrice = entryPrice * (1m + lockDistancePercent.Value / 100m);
        var end = candles[entryIdx].OpenTimeUtc.AddMinutes(horizonMinutes);
        for (var i = entryIdx; i < candles.Count; i++)
        {
            if (candles[i].OpenTimeUtc > end)
                break;
            if (candles[i].High >= targetPrice)
                return Math.Max(0, (int)Math.Round((candles[i].OpenTimeUtc - candles[entryIdx].OpenTimeUtc).TotalMinutes));
        }

        return null;
    }
}
