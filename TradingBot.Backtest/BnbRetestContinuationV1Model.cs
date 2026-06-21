using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public enum BnbRetestTargetModelName
{
    RawCurrentModelTarget,
    CappedExpectedMoveTarget,
    AtrLimitedTarget,
    RecentRangeRetestTarget
}

public sealed class BnbRetestContinuationV1Model
{
    public const string GuardPrefix = "RetestContinuation:";
    public const string ConsecutiveBullishTrendCandlesExceeded = GuardPrefix + "ConsecutiveBullishTrendCandlesExceeded";
    public const string TrendStrengthCapExceeded = GuardPrefix + "TrendStrengthCapExceeded";
    public const string DistanceToInvalidationCapExceeded = GuardPrefix + "DistanceToInvalidationCapExceeded";
    public const string PreviousCandleBearishRejected = GuardPrefix + "PreviousCandleBearishRejected";
    public const string CappedMoveTooSmall = GuardPrefix + "CappedMoveTooSmall";
    public const string NotApplicableSymbol = GuardPrefix + "NotApplicableSymbol";

    private readonly bool _enabled;
    private readonly BnbRetestTargetModelName _targetModel;
    private readonly decimal _maxCappedExpectedMovePercent;
    private readonly decimal _minCappedExpectedMovePercent;
    private readonly int _atrPeriod;
    private readonly decimal _atrMultiplier;
    private readonly int _recentMfeLookbackCandles;
    private readonly int _recentMfeForwardCandles;
    private readonly decimal _recentMfePercentile;
    private readonly int _recentRangeLookbackCandles;
    private readonly int _maxConsecutiveBullishTrendCandles;
    private readonly decimal _maxTrendStrengthPercent;
    private readonly decimal _maxDistanceToInvalidationPercent;
    private readonly bool _rejectPreviousCandleBearish;

    public BnbRetestContinuationV1Model(IConfiguration configuration)
    {
        _enabled = configuration.GetValue<bool?>("Backtest:BnbRetestContinuationV1:Enabled") ?? false;
        _targetModel = ParseTargetModel(configuration.GetValue<string?>("Backtest:BnbRetestContinuationV1:TargetModel"));
        _maxCappedExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbRetestContinuationV1:MaxCappedExpectedMovePercent") ?? 0.50m);
        _minCappedExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbRetestContinuationV1:MinCappedExpectedMovePercent") ?? 0.08m);
        _atrPeriod = Math.Max(2, configuration.GetValue<int?>("Backtest:BnbRetestContinuationV1:AtrPeriod") ?? 14);
        _atrMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbRetestContinuationV1:AtrMultiplier") ?? 1.0m);
        _recentMfeLookbackCandles = Math.Max(5, configuration.GetValue<int?>("Backtest:BnbRetestContinuationV1:RecentMfeLookbackCandles") ?? 48);
        _recentMfeForwardCandles = Math.Max(1, configuration.GetValue<int?>("Backtest:BnbRetestContinuationV1:RecentMfeForwardCandles") ?? 12);
        _recentMfePercentile = Math.Clamp(configuration.GetValue<decimal?>("Backtest:BnbRetestContinuationV1:RecentMfePercentile") ?? 50m, 1m, 99m);
        _recentRangeLookbackCandles = Math.Max(5, configuration.GetValue<int?>("Backtest:BnbRetestContinuationV1:RecentRangeLookbackCandles") ?? 24);
        _maxConsecutiveBullishTrendCandles = Math.Max(1, configuration.GetValue<int?>("Backtest:BnbRetestContinuationV1:MaxConsecutiveBullishTrendCandles") ?? 2);
        _maxTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbRetestContinuationV1:MaxTrendStrengthPercent") ?? 0.00090m);
        _maxDistanceToInvalidationPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbRetestContinuationV1:MaxDistanceToInvalidationPercent") ?? 0.40m);
        _rejectPreviousCandleBearish = configuration.GetValue<bool?>("Backtest:BnbRetestContinuationV1:RejectPreviousCandleBearish") ?? true;
    }

    public bool IsEnabled => _enabled;

    public BnbRetestContinuationDecision Evaluate(
        TradingSymbol symbol,
        StrategySignalResult signal,
        MarketSnapshot snapshot,
        string interval,
        decimal? profitLockThresholdPercent)
    {
        var entryPrice = snapshot.CurrentPrice;
        var rawExpectedMovePercent = signal.ExpectedMovePercent ?? 0m;
        var recentMfePercent = ComputeRecentMfePercentile(snapshot);
        var diagnosticsBase = new BnbRetestContinuationDiagnostics
        {
            RetestContinuationEnabled = _enabled,
            RawExpectedMovePercent = rawExpectedMovePercent,
            RecentMfePercentile = recentMfePercent,
            TargetModelName = _targetModel.ToString(),
            EntryNearRecentHigh = signal.EntryNearRecentHigh,
            ConsecutiveBullishCandlesAtEntry = signal.ConsecutiveBullishTrendCandles,
            TrendStrengthPercentAtEntry = signal.TrendStrengthPercent,
            DistanceToInvalidationPercentAtEntry = signal.DistanceToInvalidationPercent
        };

        if (!_enabled)
            return BnbRetestContinuationDecision.Allow(signal, diagnosticsBase with { RetestContinuationEnabled = false });

        if (symbol != TradingSymbol.BNBUSDT)
            return BnbRetestContinuationDecision.Block(NotApplicableSymbol, signal, diagnosticsBase);

        if (signal.ConsecutiveBullishTrendCandles is > 0
            && signal.ConsecutiveBullishTrendCandles.Value > _maxConsecutiveBullishTrendCandles)
        {
            return BnbRetestContinuationDecision.Block(
                ConsecutiveBullishTrendCandlesExceeded,
                signal,
                diagnosticsBase with { AntiChaseFilterRejected = true });
        }

        if (signal.TrendStrengthPercent is > 0m
            && signal.TrendStrengthPercent.Value > _maxTrendStrengthPercent)
        {
            return BnbRetestContinuationDecision.Block(
                TrendStrengthCapExceeded,
                signal,
                diagnosticsBase with { AntiChaseFilterRejected = true });
        }

        if (signal.DistanceToInvalidationPercent is > 0m
            && signal.DistanceToInvalidationPercent.Value > _maxDistanceToInvalidationPercent)
        {
            return BnbRetestContinuationDecision.Block(
                DistanceToInvalidationCapExceeded,
                signal,
                diagnosticsBase with { AntiChaseFilterRejected = true });
        }

        if (_rejectPreviousCandleBearish && signal.PreviousCandleBearish == true)
        {
            return BnbRetestContinuationDecision.Block(
                PreviousCandleBearishRejected,
                signal,
                diagnosticsBase with { AntiChaseFilterRejected = true });
        }

        var (cappedMovePercent, capReason, targetWasCapped, targetSource, projectionMode) =
            ResolveTargetMovePercent(signal, snapshot, entryPrice, rawExpectedMovePercent, recentMfePercent);

        if (cappedMovePercent < _minCappedExpectedMovePercent)
        {
            return BnbRetestContinuationDecision.Block(
                CappedMoveTooSmall,
                signal,
                diagnosticsBase with
                {
                    CappedExpectedMovePercent = cappedMovePercent,
                    CapReason = capReason,
                    TargetWasCapped = targetWasCapped
                });
        }

        var cappedTargetPrice = entryPrice + (entryPrice * cappedMovePercent / 100m);
        var lockDistancePercent = profitLockThresholdPercent is > 0m
            ? cappedMovePercent * profitLockThresholdPercent.Value / 100m
            : (decimal?)null;
        var expectedMoveToRecentMfeRatio = recentMfePercent is > 0m
            ? rawExpectedMovePercent / recentMfePercent.Value
            : (decimal?)null;

        var rewritten = CloneSignalWithTarget(
            signal,
            cappedTargetPrice,
            cappedMovePercent,
            targetSource,
            projectionMode);

        var diagnostics = diagnosticsBase with
        {
            CappedExpectedMovePercent = cappedMovePercent,
            RawExpectedTargetPrice = signal.ExpectedTargetPrice,
            CappedExpectedTargetPrice = cappedTargetPrice,
            LockDistancePercent = lockDistancePercent,
            TargetWasCapped = targetWasCapped,
            CapReason = capReason,
            ExpectedMoveToRecentMfeRatio = expectedMoveToRecentMfeRatio
        };

        return BnbRetestContinuationDecision.Allow(rewritten, diagnostics);
    }

    private (decimal MovePercent, string? CapReason, bool TargetWasCapped, string TargetSource, string ProjectionMode)
        ResolveTargetMovePercent(
            StrategySignalResult signal,
            MarketSnapshot snapshot,
            decimal entryPrice,
            decimal rawExpectedMovePercent,
            decimal? recentMfePercent)
    {
        return _targetModel switch
        {
            BnbRetestTargetModelName.RawCurrentModelTarget => (
                Math.Max(0m, rawExpectedMovePercent),
                null,
                false,
                signal.ExpectedTargetSource ?? "RawCurrentModelTarget",
                signal.ProjectionMode ?? "RawCurrentModelTarget"),

            BnbRetestTargetModelName.AtrLimitedTarget => CapMove(
                ComputeAtrMovePercent(snapshot, entryPrice),
                "AtrLimitedTarget",
                "BnbRetestContinuationV1.AtrLimitedTarget",
                "AtrLimitedTarget"),

            BnbRetestTargetModelName.RecentRangeRetestTarget => CapMove(
                ComputeRecentRangeRetestMovePercent(snapshot, entryPrice, recentMfePercent),
                "RecentRangeRetestTarget",
                "BnbRetestContinuationV1.RecentRangeRetestTarget",
                "RecentRangeRetestTarget"),

            _ => CapMove(
                Math.Max(0m, rawExpectedMovePercent),
                "MaxCappedExpectedMovePercent",
                "BnbRetestContinuationV1.CappedExpectedMoveTarget",
                "CappedExpectedMoveTarget")
        };

        (decimal MovePercent, string? CapReason, bool TargetWasCapped, string TargetSource, string ProjectionMode) CapMove(
            decimal proposedMove,
            string capReason,
            string targetSource,
            string projectionMode)
        {
            if (proposedMove <= _maxCappedExpectedMovePercent)
                return (proposedMove, null, false, targetSource, projectionMode);

            return (_maxCappedExpectedMovePercent, capReason, true, targetSource, projectionMode);
        }
    }

    private decimal ComputeAtrMovePercent(MarketSnapshot snapshot, decimal entryPrice)
    {
        if (entryPrice <= 0m)
            return 0m;

        var atr = ComputeAtr(snapshot, _atrPeriod);
        return atr / entryPrice * 100m * _atrMultiplier;
    }

    private decimal ComputeRecentRangeRetestMovePercent(
        MarketSnapshot snapshot,
        decimal entryPrice,
        decimal? recentMfePercent)
    {
        if (entryPrice <= 0m || snapshot.HighPrices.Count == 0)
            return 0m;

        var lookback = Math.Min(_recentRangeLookbackCandles, snapshot.HighPrices.Count);
        var start = snapshot.HighPrices.Count - lookback;
        var recentHigh = snapshot.HighPrices.Skip(start).Max();
        var rangeMove = Math.Max(0m, (recentHigh - entryPrice) / entryPrice * 100m);

        if (recentMfePercent is > 0m)
            rangeMove = Math.Min(rangeMove, recentMfePercent.Value);

        return rangeMove;
    }

    private decimal? ComputeRecentMfePercentile(MarketSnapshot snapshot)
    {
        var highs = snapshot.HighPrices;
        var closes = snapshot.ClosePrices;
        if (highs.Count < 5 || closes.Count != highs.Count)
            return null;

        var samples = new List<decimal>();
        var start = Math.Max(0, closes.Count - _recentMfeLookbackCandles);
        for (var i = start; i < closes.Count; i++)
        {
            var entry = closes[i];
            if (entry <= 0m)
                continue;

            var forwardEnd = Math.Min(highs.Count - 1, i + _recentMfeForwardCandles);
            if (forwardEnd <= i)
                continue;

            var maxHigh = highs.Skip(i + 1).Take(forwardEnd - i).DefaultIfEmpty(highs[i]).Max();
            samples.Add(Math.Max(0m, (maxHigh - entry) / entry * 100m));
        }

        if (samples.Count == 0)
            return null;

        samples.Sort();
        var index = (int)Math.Ceiling((_recentMfePercentile / 100m) * samples.Count) - 1;
        index = Math.Clamp(index, 0, samples.Count - 1);
        return samples[index];
    }

    private static decimal ComputeAtr(MarketSnapshot snapshot, int period)
    {
        var highs = snapshot.HighPrices;
        var lows = snapshot.LowPrices;
        var closes = snapshot.ClosePrices;
        if (highs.Count < 2 || lows.Count != highs.Count || closes.Count != highs.Count)
            return 0m;

        var trueRanges = new List<decimal>();
        for (var i = 1; i < highs.Count; i++)
        {
            var highLow = highs[i] - lows[i];
            var highPrevClose = Math.Abs(highs[i] - closes[i - 1]);
            var lowPrevClose = Math.Abs(lows[i] - closes[i - 1]);
            trueRanges.Add(Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose)));
        }

        if (trueRanges.Count == 0)
            return 0m;

        var window = Math.Min(period, trueRanges.Count);
        return trueRanges.Skip(trueRanges.Count - window).Average();
    }

    private static StrategySignalResult CloneSignalWithTarget(
        StrategySignalResult source,
        decimal targetPrice,
        decimal movePercent,
        string targetSource,
        string projectionMode)
    {
        return new StrategySignalResult
        {
            StrategyName = source.StrategyName,
            Signal = source.Signal,
            Reason = source.Reason,
            Confidence = source.Confidence,
            TrendConfidenceScore = source.TrendConfidenceScore,
            MarketConditionScore = source.MarketConditionScore,
            VolatilityRegime = source.VolatilityRegime,
            ExpectedTargetPrice = targetPrice,
            ExpectedMovePercent = movePercent,
            ExpectedTargetSource = targetSource,
            BreakoutRangeHigh = source.BreakoutRangeHigh,
            BreakoutRangeLow = source.BreakoutRangeLow,
            BreakoutThresholdPrice = source.BreakoutThresholdPrice,
            ExpectedTargetStructureExtensionUsed = source.ExpectedTargetStructureExtensionUsed,
            ExpectedTargetAtrUsed = source.ExpectedTargetAtrUsed,
            ConsecutiveBullishTrendCandles = source.ConsecutiveBullishTrendCandles,
            EntryNearRecentHigh = source.EntryNearRecentHigh,
            DistanceToRecentHighPercent = source.DistanceToRecentHighPercent,
            DistanceToInvalidationPercent = source.DistanceToInvalidationPercent,
            CurrentCloseAboveRecentHigh = source.CurrentCloseAboveRecentHigh,
            PreviousCandleBearish = source.PreviousCandleBearish,
            ShortMaSlopePercent = source.ShortMaSlopePercent,
            TrendStrengthPercent = source.TrendStrengthPercent,
            ProjectionMode = projectionMode,
            ProjectedExtension = source.ProjectedExtension,
            NormalTrendEntryRejectedReason = source.NormalTrendEntryRejectedReason
        };
    }

    private static BnbRetestTargetModelName ParseTargetModel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BnbRetestTargetModelName.CappedExpectedMoveTarget;

        return Enum.TryParse<BnbRetestTargetModelName>(value, ignoreCase: true, out var parsed)
            ? parsed
            : value.ToLowerInvariant() switch
            {
                "raw" or "rawcurrent" => BnbRetestTargetModelName.RawCurrentModelTarget,
                "capped" or "cappedexpectedmove" => BnbRetestTargetModelName.CappedExpectedMoveTarget,
                "atr" or "atrlimited" => BnbRetestTargetModelName.AtrLimitedTarget,
                "range" or "recentrange" or "recentrangeretest" => BnbRetestTargetModelName.RecentRangeRetestTarget,
                _ => BnbRetestTargetModelName.CappedExpectedMoveTarget
            };
    }
}

public sealed record BnbRetestContinuationDiagnostics
{
    public bool RetestContinuationEnabled { get; init; }
    public bool RetestContinuationRejected { get; init; }
    public string? RetestContinuationRejectedReason { get; init; }
    public bool AntiChaseFilterRejected { get; init; }
    public decimal? RawExpectedMovePercent { get; init; }
    public decimal? CappedExpectedMovePercent { get; init; }
    public decimal? RawExpectedTargetPrice { get; init; }
    public decimal? CappedExpectedTargetPrice { get; init; }
    public decimal? LockDistancePercent { get; init; }
    public string? TargetModelName { get; init; }
    public bool TargetWasCapped { get; init; }
    public string? CapReason { get; init; }
    public decimal? ExpectedMoveToRecentMfeRatio { get; init; }
    public decimal? RecentMfePercentile { get; init; }
    public bool? EntryNearRecentHigh { get; init; }
    public int? ConsecutiveBullishCandlesAtEntry { get; init; }
    public decimal? TrendStrengthPercentAtEntry { get; init; }
    public decimal? DistanceToInvalidationPercentAtEntry { get; init; }
}

public sealed record BnbRetestContinuationDecision
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public StrategySignalResult Signal { get; init; } = new() { Signal = TradeSignal.Hold };
    public BnbRetestContinuationDiagnostics Diagnostics { get; init; } = new();

    public static BnbRetestContinuationDecision Allow(
        StrategySignalResult signal,
        BnbRetestContinuationDiagnostics diagnostics)
        => new() { IsAllowed = true, Signal = signal, Diagnostics = diagnostics };

    public static BnbRetestContinuationDecision Block(
        string reason,
        StrategySignalResult signal,
        BnbRetestContinuationDiagnostics diagnostics)
        => new()
        {
            IsAllowed = false,
            Reason = reason,
            Signal = signal,
            Diagnostics = diagnostics with
            {
                RetestContinuationRejected = true,
                RetestContinuationRejectedReason = reason
            }
        };
}
