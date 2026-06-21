using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public enum ImpulseContinuationV1TargetModelName
{
    ImpulseMeasuredMoveTarget,
    AtrExpansionTarget,
    RecentSwingTarget,
    HybridReasonableTarget
}

public sealed class ImpulseContinuationV1Model
{
    public const string GuardPrefix = "ImpulseContinuationV1:";
    public const string InsufficientHistory = GuardPrefix + "InsufficientHistory";
    public const string NotBullishImpulse = GuardPrefix + "NotBullishImpulse";
    public const string ImpulseBodyTooWeak = GuardPrefix + "ImpulseBodyTooWeak";
    public const string ImpulseRangeTooSmall = GuardPrefix + "ImpulseRangeTooSmall";
    public const string ImpulseRangeNotExpanded = GuardPrefix + "ImpulseRangeNotExpanded";
    public const string VolumeNotExpanded = GuardPrefix + "VolumeNotExpanded";
    public const string CloseNotNearHigh = GuardPrefix + "CloseNotNearHigh";
    public const string FollowThroughFailed = GuardPrefix + "FollowThroughFailed";
    public const string WickOnlyFollowThrough = GuardPrefix + "WickOnlyFollowThrough";
    public const string AntiChaseImpulseHighExceeded = GuardPrefix + "AntiChaseImpulseHighExceeded";
    public const string AntiChaseImpulseCloseExceeded = GuardPrefix + "AntiChaseImpulseCloseExceeded";
    public const string TargetConsumedTooMuch = GuardPrefix + "TargetConsumedTooMuch";
    public const string LockBelowRequiredGross = GuardPrefix + "LockBelowRequiredGross";
    public const string TargetModelProducedNoMove = GuardPrefix + "TargetModelProducedNoMove";

    private readonly bool _enabled;
    private readonly int _lookbackCandles;
    private readonly decimal _minImpulseBodyStrengthPercent;
    private readonly decimal _minImpulseRangePercent;
    private readonly decimal _minImpulseRangeVsAverage;
    private readonly decimal _minVolumeExpansionRatio;
    private readonly decimal _maxCloseDistanceFromHighPercent;
    private readonly decimal _minFollowThroughCloseStrengthPercent;
    private readonly decimal _maxDistanceFromImpulseHighPercent;
    private readonly decimal _maxDistanceFromImpulseClosePercent;
    private readonly decimal _maxTargetConsumedPercent;
    private readonly ImpulseContinuationV1TargetModelName _targetModelName;
    private readonly decimal _impulseMeasuredMoveMultiplier;
    private readonly decimal _atrExpansionTargetMultiplier;
    private readonly decimal _recentSwingLookbackCandles;
    private readonly decimal _hybridMaxExpectedMovePercent;
    private readonly decimal _requiredNetProfitPercent;
    private readonly int _atrPeriod;
    private readonly Dictionary<string, decimal> _maxExpectedMovePercentByInterval;

    private PendingImpulseV1? _pending;

    public ImpulseContinuationV1Model(IConfiguration configuration)
    {
        _enabled = configuration.GetValue<bool?>("Backtest:ImpulseContinuationV1:Enabled") ?? false;
        _lookbackCandles = Math.Max(3, configuration.GetValue<int?>("Backtest:ImpulseContinuationV1:LookbackCandles") ?? 12);
        _minImpulseBodyStrengthPercent = Math.Clamp(
            configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:MinImpulseBodyStrengthPercent") ?? 65m,
            0m,
            100m);
        _minImpulseRangePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:MinImpulseRangePercent") ?? 0.12m);
        _minImpulseRangeVsAverage = Math.Max(1m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:MinImpulseRangeVsAverage") ?? 1.30m);
        _minVolumeExpansionRatio = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:MinVolumeExpansionRatio") ?? 1.25m);
        _maxCloseDistanceFromHighPercent = Math.Clamp(
            configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:MaxCloseDistanceFromHighPercent") ?? 15m,
            0m,
            100m);
        _minFollowThroughCloseStrengthPercent = Math.Clamp(
            configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:MinFollowThroughCloseStrengthPercent") ?? 50m,
            0m,
            100m);
        _maxDistanceFromImpulseHighPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:MaxDistanceFromImpulseHighPercent") ?? 0.18m);
        _maxDistanceFromImpulseClosePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:MaxDistanceFromImpulseClosePercent") ?? 0.12m);
        _maxTargetConsumedPercent = Math.Clamp(
            configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:MaxTargetConsumedPercent") ?? 25m,
            0m,
            100m);
        _targetModelName = ParseTargetModel(configuration.GetValue<string?>("Backtest:ImpulseContinuationV1:TargetModelName"));
        _impulseMeasuredMoveMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:ImpulseMeasuredMoveMultiplier") ?? 1.75m);
        _atrExpansionTargetMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:AtrExpansionTargetMultiplier") ?? 2.25m);
        _recentSwingLookbackCandles = Math.Max(3m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:RecentSwingLookbackCandles") ?? 20m);
        _hybridMaxExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:HybridMaxExpectedMovePercent") ?? 1.50m);
        _requiredNetProfitPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ImpulseContinuationV1:RequiredNetProfitPercent") ?? 0.10m);
        _atrPeriod = Math.Max(2, configuration.GetValue<int?>("Backtest:ImpulseContinuationV1:AtrPeriod") ?? 14);
        _maxExpectedMovePercentByInterval = ReadIntervalMap(configuration, "MaxExpectedMovePercentByInterval", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 1.50m,
            ["3m"] = 1.80m,
            ["5m"] = 2.20m
        });
    }

    public bool IsEnabled => _enabled;
    public int MinRequiredCandles => _lookbackCandles + 2;
    public decimal RequiredNetProfitPercent => _requiredNetProfitPercent;

    public void Reset() => _pending = null;

    public ImpulseContinuationV1StepResult ProcessCandle(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        TradingSymbol symbol,
        decimal? profitLockThresholdPercent,
        ExecutionCostSettings executionCosts,
        IConfiguration configuration)
    {
        if (!_enabled || index < 0 || index >= candles.Count)
            return ImpulseContinuationV1StepResult.NoAction();

        if (_pending is not null)
        {
            var followThroughResult = EvaluateFollowThrough(
                candles, index, interval, profitLockThresholdPercent, executionCosts);
            if (followThroughResult is not null)
                return followThroughResult;
        }

        if (_pending is null)
            TryDetectImpulse(candles, index, interval);

        return ImpulseContinuationV1StepResult.NoAction();
    }

    private ImpulseContinuationV1StepResult? EvaluateFollowThrough(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        decimal? profitLockThresholdPercent,
        ExecutionCostSettings executionCosts)
    {
        var pending = _pending!;
        if (index != pending.ImpulseIndex + 1)
        {
            _pending = null;
            return ImpulseContinuationV1StepResult.Blocked(
                FollowThroughFailed,
                BuildDiagnostics(pending, candles[index], interval) with
                {
                    FollowThroughConfirmed = false,
                    RejectionReason = FollowThroughFailed
                });
        }

        var impulseCandle = candles[pending.ImpulseIndex];
        var followCandle = candles[index];
        var followThroughConfirmed = followCandle.Close > impulseCandle.High || followCandle.Close > impulseCandle.Close;

        var followRange = followCandle.High - followCandle.Low;
        var followThroughCloseStrengthPercent = followRange > 0m
            ? (followCandle.Close - followCandle.Low) / followRange * 100m
            : 0m;

        var diagnosticsBase = BuildDiagnostics(pending, followCandle, interval) with
        {
            FollowThroughConfirmed = followThroughConfirmed,
            FollowThroughCloseStrengthPercent = Math.Round(followThroughCloseStrengthPercent, 6)
        };

        _pending = null;

        if (!followThroughConfirmed)
        {
            return ImpulseContinuationV1StepResult.Blocked(
                FollowThroughFailed,
                diagnosticsBase with { RejectionReason = FollowThroughFailed });
        }

        if (followThroughCloseStrengthPercent < _minFollowThroughCloseStrengthPercent)
        {
            return ImpulseContinuationV1StepResult.Blocked(
                WickOnlyFollowThrough,
                diagnosticsBase with { RejectionReason = WickOnlyFollowThrough });
        }

        var entryPrice = followCandle.Close;
        var distanceFromImpulseHighPercent = impulseCandle.High > 0m
            ? (entryPrice - impulseCandle.High) / entryPrice * 100m
            : 0m;
        diagnosticsBase = diagnosticsBase with
        {
            EntryDistanceFromImpulseHighPercent = Math.Round(distanceFromImpulseHighPercent, 6)
        };

        if (distanceFromImpulseHighPercent > _maxDistanceFromImpulseHighPercent)
        {
            return ImpulseContinuationV1StepResult.Blocked(
                AntiChaseImpulseHighExceeded,
                diagnosticsBase with { RejectionReason = AntiChaseImpulseHighExceeded });
        }

        var distanceFromImpulseClosePercent = impulseCandle.Close > 0m
            ? (entryPrice - impulseCandle.Close) / entryPrice * 100m
            : 0m;
        if (distanceFromImpulseClosePercent > _maxDistanceFromImpulseClosePercent)
        {
            return ImpulseContinuationV1StepResult.Blocked(
                AntiChaseImpulseCloseExceeded,
                diagnosticsBase with { RejectionReason = AntiChaseImpulseCloseExceeded });
        }

        var roundTrip = RangeExpansionCostModel.ComputeRoundTripCostPercent(executionCosts);
        var requiredGross = roundTrip + _requiredNetProfitPercent;

        var (expectedMovePercent, targetModelName, targetWasCapped, capReason) =
            ResolveTargetMovePercent(pending, candles, index, interval, entryPrice, requiredGross);

        diagnosticsBase = diagnosticsBase with
        {
            ExpectedMovePercent = expectedMovePercent,
            TargetModelName = targetModelName.ToString(),
            TargetWasCapped = targetWasCapped,
            CapReason = capReason,
            EstimatedRoundTripCostPercent = roundTrip,
            RequiredNetProfitPercent = _requiredNetProfitPercent,
            RequiredGrossMovePercent = requiredGross
        };

        if (!expectedMovePercent.HasValue || expectedMovePercent.Value <= 0m)
        {
            return ImpulseContinuationV1StepResult.Blocked(
                TargetModelProducedNoMove,
                diagnosticsBase with { RejectionReason = TargetModelProducedNoMove });
        }

        var targetConsumedPercent = expectedMovePercent.Value > 0m
            ? Math.Round((distanceFromImpulseClosePercent / expectedMovePercent.Value) * 100m, 6)
            : 0m;
        diagnosticsBase = diagnosticsBase with { TargetConsumedPercent = targetConsumedPercent };

        if (targetConsumedPercent > _maxTargetConsumedPercent)
        {
            return ImpulseContinuationV1StepResult.Blocked(
                TargetConsumedTooMuch,
                diagnosticsBase with { RejectionReason = TargetConsumedTooMuch });
        }

        var lock90 = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 90m);
        var activeLockDistance = profitLockThresholdPercent switch
        {
            90m => lock90,
            95m => CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 95m),
            98m => CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 98m),
            _ => lock90
        };

        var stopDistancePercent = ResolveStopDistancePercent(entryPrice, pending);
        var stopToLockRatio = lock90 is > 0m && stopDistancePercent.HasValue
            ? Math.Round(stopDistancePercent.Value / lock90.Value, 6)
            : (decimal?)null;

        diagnosticsBase = diagnosticsBase with
        {
            Lock90DistancePercent = lock90,
            Lock90NetProfitPercent = lock90.HasValue ? lock90.Value - roundTrip : null,
            StopDistancePercent = stopDistancePercent,
            StopToLockRatio = stopToLockRatio
        };

        if (activeLockDistance is null or <= 0m || activeLockDistance.Value < requiredGross)
        {
            return ImpulseContinuationV1StepResult.Blocked(
                LockBelowRequiredGross,
                diagnosticsBase with { RejectionReason = LockBelowRequiredGross });
        }

        var targetPrice = entryPrice + (entryPrice * expectedMovePercent.Value / 100m);
        var distanceToInvalidationPercent = pending.ImpulseLow > 0m
            ? (entryPrice - pending.ImpulseLow) / entryPrice * 100m
            : (decimal?)null;

        var signal = new StrategySignalResult
        {
            StrategyName = "ImpulseContinuationV1",
            Signal = TradeSignal.Buy,
            Reason = "Impulse continuation with cost-aware larger-move target.",
            Confidence = 0.82m,
            ExpectedTargetPrice = targetPrice,
            ExpectedMovePercent = expectedMovePercent,
            ExpectedTargetSource = "ImpulseContinuationV1." + targetModelName,
            BreakoutRangeHigh = pending.ImpulseHigh,
            BreakoutRangeLow = pending.ImpulseLow,
            BreakoutThresholdPrice = pending.ImpulseHigh,
            DistanceToInvalidationPercent = distanceToInvalidationPercent,
            ProjectionMode = targetModelName.ToString(),
            VolatilityRegime = pending.ImpulseRangeVsAverage >= _minImpulseRangeVsAverage ? "Expanded" : "Normal"
        };

        return ImpulseContinuationV1StepResult.Entry(
            signal,
            diagnosticsBase with { RejectionReason = null });
    }

    private void TryDetectImpulse(IReadOnlyList<KlineCandle> candles, int index, string interval)
    {
        if (index < _lookbackCandles)
            return;

        var impulseCandle = candles[index];
        if (impulseCandle.Close <= impulseCandle.Open)
            return;

        var rangeStart = index - _lookbackCandles;
        var rangeEnd = index - 1;
        if (rangeStart < 0 || rangeEnd < rangeStart)
            return;

        decimal avgRange = 0m;
        decimal avgVolume = 0m;
        var count = rangeEnd - rangeStart + 1;
        for (var i = rangeStart; i <= rangeEnd; i++)
        {
            avgRange += candles[i].High - candles[i].Low;
            avgVolume += candles[i].Volume;
        }

        avgRange /= count;
        avgVolume /= count;

        var candleRange = impulseCandle.High - impulseCandle.Low;
        if (candleRange <= 0m)
            return;

        var impulseRangePercent = impulseCandle.Close > 0m ? candleRange / impulseCandle.Close * 100m : 0m;
        if (impulseRangePercent < _minImpulseRangePercent)
            return;

        var impulseRangeVsAverage = avgRange > 0m ? candleRange / avgRange : 0m;
        if (impulseRangeVsAverage < _minImpulseRangeVsAverage)
            return;

        var body = impulseCandle.Close - impulseCandle.Open;
        var impulseBodyStrengthPercent = body / candleRange * 100m;
        if (impulseBodyStrengthPercent < _minImpulseBodyStrengthPercent)
            return;

        var volumeExpansionRatio = avgVolume > 0m ? impulseCandle.Volume / avgVolume : 0m;
        if (volumeExpansionRatio < _minVolumeExpansionRatio)
            return;

        var closeNearHighPercent = (impulseCandle.High - impulseCandle.Close) / candleRange * 100m;
        if (closeNearHighPercent > _maxCloseDistanceFromHighPercent)
            return;

        var atr = ComputeAtr(candles, index, _atrPeriod);
        var atrPercent = impulseCandle.Close > 0m ? atr / impulseCandle.Close * 100m : 0m;

        _pending = new PendingImpulseV1(
            index,
            impulseCandle.Open,
            impulseCandle.Close,
            impulseCandle.High,
            impulseCandle.Low,
            impulseRangePercent,
            impulseRangeVsAverage,
            impulseBodyStrengthPercent,
            volumeExpansionRatio,
            closeNearHighPercent,
            atrPercent);
    }

    private (decimal? MovePercent, ImpulseContinuationV1TargetModelName TargetModel, bool TargetWasCapped, string? CapReason)
        ResolveTargetMovePercent(
            PendingImpulseV1 pending,
            IReadOnlyList<KlineCandle> candles,
            int index,
            string interval,
            decimal entryPrice,
            decimal requiredGross)
    {
        var intervalCap = ResolveIntervalCap(_maxExpectedMovePercentByInterval, interval, 1.50m);
        var recentSwingMove = ComputeRecentSwingTargetPercent(candles, index, entryPrice);

        decimal? proposed = _targetModelName switch
        {
            ImpulseContinuationV1TargetModelName.ImpulseMeasuredMoveTarget =>
                pending.ImpulseRangePercent * _impulseMeasuredMoveMultiplier,
            ImpulseContinuationV1TargetModelName.AtrExpansionTarget =>
                pending.AtrPercent * _atrExpansionTargetMultiplier,
            ImpulseContinuationV1TargetModelName.RecentSwingTarget =>
                recentSwingMove,
            _ => Math.Max(
                pending.ImpulseRangePercent * _impulseMeasuredMoveMultiplier,
                Math.Max(
                    pending.AtrPercent * _atrExpansionTargetMultiplier,
                    recentSwingMove ?? 0m))
        };

        if (_targetModelName == ImpulseContinuationV1TargetModelName.HybridReasonableTarget)
            proposed = Math.Min(proposed ?? 0m, _hybridMaxExpectedMovePercent);

        if (!proposed.HasValue || proposed.Value <= 0m)
            return (null, _targetModelName, false, "NoMove");

        proposed = Math.Max(proposed.Value, requiredGross * 100m / 90m);

        var wasCapped = false;
        string? capReason = null;
        if (proposed > intervalCap)
        {
            proposed = intervalCap;
            wasCapped = true;
            capReason = "IntervalCap";
        }

        return (Math.Round(proposed.Value, 6), _targetModelName, wasCapped, capReason);
    }

    private decimal? ComputeRecentSwingTargetPercent(IReadOnlyList<KlineCandle> candles, int index, decimal entryPrice)
    {
        if (entryPrice <= 0m)
            return null;

        var lookback = (int)_recentSwingLookbackCandles;
        var start = Math.Max(0, index - lookback);
        decimal swingHigh = decimal.MinValue;
        for (var i = start; i <= index; i++)
            swingHigh = Math.Max(swingHigh, candles[i].High);

        if (swingHigh <= entryPrice)
            return null;

        return Math.Round((swingHigh - entryPrice) / entryPrice * 100m, 6);
    }

    private decimal? ResolveStopDistancePercent(decimal entryPrice, PendingImpulseV1 pending)
    {
        if (entryPrice <= 0m)
            return null;

        var stopPrice = pending.ImpulseLow;
        return Math.Round((entryPrice - stopPrice) / entryPrice * 100m, 6);
    }

    private ImpulseContinuationV1Diagnostics BuildDiagnostics(
        PendingImpulseV1 pending,
        KlineCandle candle,
        string interval)
        => new()
        {
            ImpulseOpen = pending.ImpulseOpen,
            ImpulseClose = pending.ImpulseClose,
            ImpulseHigh = pending.ImpulseHigh,
            ImpulseLow = pending.ImpulseLow,
            ImpulseBodyStrengthPercent = pending.ImpulseBodyStrengthPercent,
            ImpulseRangePercent = pending.ImpulseRangePercent,
            ImpulseRangeVsAverage = pending.ImpulseRangeVsAverage,
            VolumeExpansionRatio = pending.VolumeExpansionRatio,
            CloseNearHighPercent = pending.CloseNearHighPercent,
            AtrPercent = pending.AtrPercent
        };

    private static ImpulseContinuationV1TargetModelName ParseTargetModel(string? raw)
        => Enum.TryParse<ImpulseContinuationV1TargetModelName>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : ImpulseContinuationV1TargetModelName.HybridReasonableTarget;

    private static decimal ComputeAtr(IReadOnlyList<KlineCandle> candles, int lastIndexInclusive, int period)
    {
        if (lastIndexInclusive < 1)
            return 0m;

        var trueRanges = new List<decimal>();
        var start = Math.Max(1, lastIndexInclusive - period + 1);
        for (var i = start; i <= lastIndexInclusive; i++)
        {
            var highLow = candles[i].High - candles[i].Low;
            var highPrevClose = Math.Abs(candles[i].High - candles[i - 1].Close);
            var lowPrevClose = Math.Abs(candles[i].Low - candles[i - 1].Close);
            trueRanges.Add(Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose)));
        }

        return trueRanges.Count == 0 ? 0m : trueRanges.Average();
    }

    private static decimal ResolveIntervalCap(IReadOnlyDictionary<string, decimal> map, string interval, decimal fallback)
        => map.TryGetValue(interval, out var value) ? value : fallback;

    private static Dictionary<string, decimal> ReadIntervalMap(
        IConfiguration configuration,
        string suffix,
        Dictionary<string, decimal> defaults)
    {
        foreach (var interval in new[] { "1m", "3m", "5m" })
        {
            var configured = configuration.GetValue<decimal?>($"Backtest:ImpulseContinuationV1:{suffix}:{interval}");
            if (configured.HasValue)
                defaults[interval] = configured.Value;
        }

        return defaults;
    }

    private sealed record PendingImpulseV1(
        int ImpulseIndex,
        decimal ImpulseOpen,
        decimal ImpulseClose,
        decimal ImpulseHigh,
        decimal ImpulseLow,
        decimal ImpulseRangePercent,
        decimal ImpulseRangeVsAverage,
        decimal ImpulseBodyStrengthPercent,
        decimal VolumeExpansionRatio,
        decimal CloseNearHighPercent,
        decimal AtrPercent);
}

public sealed record ImpulseContinuationV1Diagnostics
{
    public decimal ImpulseOpen { get; init; }
    public decimal ImpulseClose { get; init; }
    public decimal ImpulseHigh { get; init; }
    public decimal ImpulseLow { get; init; }
    public decimal ImpulseBodyStrengthPercent { get; init; }
    public decimal ImpulseRangePercent { get; init; }
    public decimal ImpulseRangeVsAverage { get; init; }
    public decimal VolumeExpansionRatio { get; init; }
    public decimal CloseNearHighPercent { get; init; }
    public decimal AtrPercent { get; init; }
    public bool FollowThroughConfirmed { get; init; }
    public decimal? FollowThroughCloseStrengthPercent { get; init; }
    public decimal? EntryDistanceFromImpulseHighPercent { get; init; }
    public decimal? TargetConsumedPercent { get; init; }
    public string? TargetModelName { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? Lock90DistancePercent { get; init; }
    public decimal? Lock90NetProfitPercent { get; init; }
    public decimal? EstimatedRoundTripCostPercent { get; init; }
    public decimal? RequiredNetProfitPercent { get; init; }
    public decimal? RequiredGrossMovePercent { get; init; }
    public bool TargetWasCapped { get; init; }
    public string? CapReason { get; init; }
    public decimal? StopDistancePercent { get; init; }
    public decimal? StopToLockRatio { get; init; }
    public string? RejectionReason { get; init; }
}

public sealed record ImpulseContinuationV1StepResult
{
    public ImpulseContinuationV1StepKind Kind { get; init; }
    public StrategySignalResult Signal { get; init; } = new() { Signal = TradeSignal.Hold };
    public ImpulseContinuationV1Diagnostics Diagnostics { get; init; } = new();
    public string? RejectionReason { get; init; }

    public static ImpulseContinuationV1StepResult NoAction()
        => new() { Kind = ImpulseContinuationV1StepKind.NoAction };

    public static ImpulseContinuationV1StepResult Entry(StrategySignalResult signal, ImpulseContinuationV1Diagnostics diagnostics)
        => new() { Kind = ImpulseContinuationV1StepKind.Entry, Signal = signal, Diagnostics = diagnostics };

    public static ImpulseContinuationV1StepResult Blocked(string reason, ImpulseContinuationV1Diagnostics diagnostics)
        => new() { Kind = ImpulseContinuationV1StepKind.Blocked, RejectionReason = reason, Diagnostics = diagnostics with { RejectionReason = reason } };
}

public enum ImpulseContinuationV1StepKind
{
    NoAction,
    Blocked,
    Entry
}
