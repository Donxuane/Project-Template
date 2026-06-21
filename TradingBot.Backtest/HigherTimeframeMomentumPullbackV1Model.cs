using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public enum HigherTimeframeMomentumPullbackV1TargetModelName
{
    RecentSwingHighTarget,
    AtrMultipleTarget,
    TrendContinuationMeasuredMoveTarget,
    HybridMinReasonableTarget
}

public enum HigherTimeframeMomentumPullbackV1StopModeName
{
    PullbackLow,
    AtrStop,
    MediumMaInvalidation
}

public sealed class HigherTimeframeMomentumPullbackV1Model
{
    public const string GuardPrefix = "HigherTimeframeMomentumPullbackV1:";
    public const string InsufficientHistory = GuardPrefix + "InsufficientHistory";
    public const string TrendNotUp = GuardPrefix + "TrendNotUp";
    public const string TrendTooFlat = GuardPrefix + "TrendTooFlat";
    public const string TrendTooExtended = GuardPrefix + "TrendTooExtended";
    public const string VolatilityTooHigh = GuardPrefix + "VolatilityTooHigh";
    public const string PullbackTooShallow = GuardPrefix + "PullbackTooShallow";
    public const string PullbackTooDeep = GuardPrefix + "PullbackTooDeep";
    public const string PullbackBreakdown = GuardPrefix + "PullbackBreakdown";
    public const string ReclaimFailed = GuardPrefix + "ReclaimFailed";
    public const string VolumeNotConfirmed = GuardPrefix + "VolumeNotConfirmed";
    public const string TargetBelowRequiredGross = GuardPrefix + "TargetBelowRequiredGross";
    public const string TargetBelowMinMoveFloor = GuardPrefix + "TargetBelowMinMoveFloor";
    public const string TargetModelProducedNoMove = GuardPrefix + "TargetModelProducedNoMove";

    private readonly bool _enabled;
    private readonly int _mediumMaPeriod;
    private readonly int _shortMaPeriod;
    private readonly int _trendSlopeLookback;
    private readonly decimal _minTrendSlopePercent;
    private readonly decimal _minTrendStrengthPercent;
    private readonly decimal _maxTrendStrengthPercent;
    private readonly decimal _maxAtrPercent;
    private readonly int _swingLookbackCandles;
    private readonly decimal _minPullbackDepthPercent;
    private readonly decimal _maxPullbackDepthPercent;
    private readonly decimal _maxDistanceToMediumMaPercent;
    private readonly decimal _mediumMaInvalidationBufferPercent;
    private readonly int _maxPullbackWaitCandles;
    private readonly bool _requireVolumeConfirmation;
    private readonly decimal _minVolumeExpansionRatio;
    private readonly HigherTimeframeMomentumPullbackV1TargetModelName _targetModelName;
    private readonly decimal _atrTargetMultiplier;
    private readonly decimal _trendContinuationMultiplier;
    private readonly decimal _hybridMinMovePercent;
    private readonly decimal _requiredNetProfitPercent;
    private readonly HigherTimeframeMomentumPullbackV1StopModeName _stopMode;
    private readonly decimal _stopBufferPercent;
    private readonly decimal _atrStopMultiplier;
    private readonly int _atrPeriod;
    private readonly Dictionary<string, decimal> _minExpectedMovePercentByInterval;

    private PendingHtfPullbackV1? _pending;

    public HigherTimeframeMomentumPullbackV1Model(IConfiguration configuration)
    {
        _enabled = configuration.GetValue<bool?>("Backtest:HigherTimeframeMomentumPullbackV1:Enabled") ?? false;
        _mediumMaPeriod = Math.Max(5, configuration.GetValue<int?>("Backtest:HigherTimeframeMomentumPullbackV1:MediumMaPeriod") ?? 20);
        _shortMaPeriod = Math.Max(3, configuration.GetValue<int?>("Backtest:HigherTimeframeMomentumPullbackV1:ShortMaPeriod") ?? 8);
        _trendSlopeLookback = Math.Max(2, configuration.GetValue<int?>("Backtest:HigherTimeframeMomentumPullbackV1:TrendSlopeLookback") ?? 5);
        _minTrendSlopePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:MinTrendSlopePercent") ?? 0.03m);
        _minTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:MinTrendStrengthPercent") ?? 0.08m);
        _maxTrendStrengthPercent = Math.Max(_minTrendStrengthPercent, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:MaxTrendStrengthPercent") ?? 2.50m);
        _maxAtrPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:MaxAtrPercent") ?? 2.50m);
        _swingLookbackCandles = Math.Max(5, configuration.GetValue<int?>("Backtest:HigherTimeframeMomentumPullbackV1:SwingLookbackCandles") ?? 12);
        _minPullbackDepthPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:MinPullbackDepthPercent") ?? 0.20m);
        _maxPullbackDepthPercent = Math.Max(_minPullbackDepthPercent, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:MaxPullbackDepthPercent") ?? 1.80m);
        _maxDistanceToMediumMaPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:MaxDistanceToMediumMaPercent") ?? 0.90m);
        _mediumMaInvalidationBufferPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:MediumMaInvalidationBufferPercent") ?? 0.08m);
        _maxPullbackWaitCandles = Math.Max(1, configuration.GetValue<int?>("Backtest:HigherTimeframeMomentumPullbackV1:MaxPullbackWaitCandles") ?? 4);
        _requireVolumeConfirmation = configuration.GetValue<bool?>("Backtest:HigherTimeframeMomentumPullbackV1:RequireVolumeConfirmation") ?? false;
        _minVolumeExpansionRatio = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:MinVolumeExpansionRatio") ?? 1.05m);
        _targetModelName = ParseTargetModel(configuration.GetValue<string?>("Backtest:HigherTimeframeMomentumPullbackV1:TargetModelName"));
        _atrTargetMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:AtrTargetMultiplier") ?? 2.50m);
        _trendContinuationMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:TrendContinuationMultiplier") ?? 1.50m);
        _hybridMinMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:HybridMinMovePercent") ?? 0.80m);
        _requiredNetProfitPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:RequiredNetProfitPercent") ?? 0.50m);
        _stopMode = ParseStopMode(configuration.GetValue<string?>("Backtest:HigherTimeframeMomentumPullbackV1:StopMode"));
        _stopBufferPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:StopBufferPercent") ?? 0.08m);
        _atrStopMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:HigherTimeframeMomentumPullbackV1:AtrStopMultiplier") ?? 1.50m);
        _atrPeriod = Math.Max(2, configuration.GetValue<int?>("Backtest:HigherTimeframeMomentumPullbackV1:AtrPeriod") ?? 14);
        _minExpectedMovePercentByInterval = ReadIntervalMap(configuration, "MinExpectedMovePercentByInterval", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["15m"] = 0.80m,
            ["30m"] = 1.00m
        });
    }

    public bool IsEnabled => _enabled;
    public int MinRequiredCandles => _mediumMaPeriod + _trendSlopeLookback + _swingLookbackCandles + 2;
    public decimal RequiredNetProfitPercent => _requiredNetProfitPercent;

    public void Reset() => _pending = null;

    public HigherTimeframeMomentumPullbackV1StepResult ProcessCandle(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        TradingSymbol symbol,
        decimal? profitLockThresholdPercent,
        ExecutionCostSettings executionCosts,
        IConfiguration configuration)
    {
        if (!_enabled || index < 0 || index >= candles.Count)
            return HigherTimeframeMomentumPullbackV1StepResult.NoAction();

        if (_pending is not null)
        {
            var reclaimResult = EvaluateReclaim(candles, index, interval, profitLockThresholdPercent, executionCosts);
            if (reclaimResult is not null)
                return reclaimResult;
        }

        if (_pending is null)
            TryDetectPullback(candles, index, interval);

        return HigherTimeframeMomentumPullbackV1StepResult.NoAction();
    }

    private HigherTimeframeMomentumPullbackV1StepResult? EvaluateReclaim(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        decimal? profitLockThresholdPercent,
        ExecutionCostSettings executionCosts)
    {
        var pending = _pending!;
        var candle = candles[index];
        if (index > pending.DetectedIndex + _maxPullbackWaitCandles)
        {
            _pending = null;
            return HigherTimeframeMomentumPullbackV1StepResult.Blocked(
                ReclaimFailed,
                BuildDiagnostics(pending, candle, interval) with { RejectionReason = ReclaimFailed });
        }

        var mediumMa = ComputeSma(candles, index, _mediumMaPeriod);
        if (mediumMa is null)
        {
            _pending = null;
            return HigherTimeframeMomentumPullbackV1StepResult.Blocked(
                PullbackBreakdown,
                BuildDiagnostics(pending, candle, interval) with { RejectionReason = PullbackBreakdown });
        }

        var invalidationPrice = mediumMa.Value * (1m - _mediumMaInvalidationBufferPercent / 100m);
        if (candle.Low < invalidationPrice)
        {
            _pending = null;
            return HigherTimeframeMomentumPullbackV1StepResult.Blocked(
                PullbackBreakdown,
                BuildDiagnostics(pending, candle, interval) with { RejectionReason = PullbackBreakdown });
        }

        var shortMa = ComputeSma(candles, index, _shortMaPeriod);
        var prevCandle = candles[index - 1];
        var bullish = candle.Close > candle.Open;
        var reclaimConfirmed = bullish
            && (candle.Close > prevCandle.High || (shortMa.HasValue && candle.Close > shortMa.Value));

        var diagnostics = BuildDiagnostics(pending, candle, interval) with
        {
            ReclaimConfirmed = reclaimConfirmed,
            DistanceToMediumMaPercent = mediumMa.Value > 0m
                ? Math.Round(Math.Abs(candle.Close - mediumMa.Value) / candle.Close * 100m, 6)
                : null,
            DistanceToShortMaPercent = shortMa.HasValue && candle.Close > 0m
                ? Math.Round(Math.Abs(candle.Close - shortMa.Value) / candle.Close * 100m, 6)
                : null
        };

        if (!reclaimConfirmed)
            return null;

        if (_requireVolumeConfirmation)
        {
            var avgVolume = ComputeAverageVolume(candles, index - 1, 10);
            var volumeRatio = avgVolume > 0m ? candle.Volume / avgVolume : 0m;
            diagnostics = diagnostics with { VolumeExpansionRatio = Math.Round(volumeRatio, 6) };
            if (volumeRatio < _minVolumeExpansionRatio)
            {
                _pending = null;
                return HigherTimeframeMomentumPullbackV1StepResult.Blocked(
                    VolumeNotConfirmed,
                    diagnostics with { RejectionReason = VolumeNotConfirmed });
            }
        }

        _pending = null;
        return EvaluateEntry(candles, index, interval, pending, candle, diagnostics, profitLockThresholdPercent, executionCosts);
    }

    private HigherTimeframeMomentumPullbackV1StepResult EvaluateEntry(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        PendingHtfPullbackV1 pending,
        KlineCandle entryCandle,
        HigherTimeframeMomentumPullbackV1Diagnostics diagnostics,
        decimal? profitLockThresholdPercent,
        ExecutionCostSettings executionCosts)
    {
        var entryPrice = entryCandle.Close;
        var roundTrip = RangeExpansionCostModel.ComputeRoundTripCostPercent(executionCosts);
        var requiredGross = roundTrip + _requiredNetProfitPercent;
        var minMoveFloor = ResolveIntervalCap(_minExpectedMovePercentByInterval, interval, _hybridMinMovePercent);

        var (expectedMovePercent, targetModelName, capReason) =
            ResolveTargetMovePercent(pending, candles, index, interval, entryPrice, requiredGross, minMoveFloor);

        diagnostics = diagnostics with
        {
            ExpectedMovePercent = expectedMovePercent,
            TargetModelName = targetModelName.ToString(),
            EstimatedRoundTripCostPercent = roundTrip,
            RequiredNetProfitPercent = _requiredNetProfitPercent,
            RequiredGrossMovePercent = requiredGross,
            MinExpectedMoveFloorPercent = minMoveFloor,
            CapReason = capReason
        };

        if (!expectedMovePercent.HasValue || expectedMovePercent.Value <= 0m)
        {
            return HigherTimeframeMomentumPullbackV1StepResult.Blocked(
                TargetModelProducedNoMove,
                diagnostics with { RejectionReason = TargetModelProducedNoMove });
        }

        if (expectedMovePercent.Value < minMoveFloor)
        {
            return HigherTimeframeMomentumPullbackV1StepResult.Blocked(
                TargetBelowMinMoveFloor,
                diagnostics with { RejectionReason = TargetBelowMinMoveFloor });
        }

        var lock90 = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 90m);
        var activeLockDistance = profitLockThresholdPercent switch
        {
            90m => lock90,
            95m => CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 95m),
            98m => CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 98m),
            100m => expectedMovePercent,
            _ => expectedMovePercent
        };

        var stopPrice = ResolveStopPrice(entryPrice, pending, diagnostics.AtrPercent);
        var stopDistancePercent = stopPrice > 0m && entryPrice > stopPrice
            ? Math.Round((entryPrice - stopPrice) / entryPrice * 100m, 6)
            : (decimal?)null;
        var rewardRisk = stopDistancePercent is > 0m
            ? Math.Round(expectedMovePercent.Value / stopDistancePercent.Value, 6)
            : (decimal?)null;

        diagnostics = diagnostics with
        {
            StopDistancePercent = stopDistancePercent,
            RewardRisk = rewardRisk,
            Lock90DistancePercent = lock90
        };

        if (activeLockDistance is null or <= 0m || activeLockDistance.Value < requiredGross)
        {
            return HigherTimeframeMomentumPullbackV1StepResult.Blocked(
                TargetBelowRequiredGross,
                diagnostics with { RejectionReason = TargetBelowRequiredGross });
        }

        var targetPrice = entryPrice * (1m + expectedMovePercent.Value / 100m);
        var signal = new StrategySignalResult
        {
            StrategyName = "HigherTimeframeMomentumPullbackV1",
            Signal = TradeSignal.Buy,
            Reason = "HTF uptrend pullback reclaim with cost-aware larger-move target.",
            Confidence = 0.80m,
            ExpectedTargetPrice = targetPrice,
            ExpectedMovePercent = expectedMovePercent,
            ExpectedTargetSource = "HigherTimeframeMomentumPullbackV1." + targetModelName,
            BreakoutRangeHigh = pending.SwingHigh,
            BreakoutRangeLow = stopPrice,
            BreakoutThresholdPrice = pending.SwingHigh,
            DistanceToInvalidationPercent = stopDistancePercent,
            ShortMaSlopePercent = diagnostics.TrendSlopePercent,
            TrendStrengthPercent = diagnostics.TrendStrengthPercent,
            ProjectionMode = targetModelName.ToString(),
            VolatilityRegime = diagnostics.AtrPercent <= _maxAtrPercent ? "Normal" : "Elevated"
        };

        return HigherTimeframeMomentumPullbackV1StepResult.Entry(signal, diagnostics with { RejectionReason = null });
    }

    private void TryDetectPullback(IReadOnlyList<KlineCandle> candles, int index, string interval)
    {
        if (index < MinRequiredCandles)
            return;

        var candle = candles[index];
        var mediumMa = ComputeSma(candles, index, _mediumMaPeriod);
        var shortMa = ComputeSma(candles, index, _shortMaPeriod);
        if (mediumMa is null || shortMa is null)
            return;

        var priorMediumMa = ComputeSma(candles, index - _trendSlopeLookback, _mediumMaPeriod);
        if (priorMediumMa is null || candle.Close <= 0m)
            return;

        var trendSlopePercent = (mediumMa.Value - priorMediumMa.Value) / candle.Close * 100m;
        var trendStrengthPercent = (candle.Close - mediumMa.Value) / candle.Close * 100m;
        if (candle.Close <= mediumMa.Value || trendSlopePercent < _minTrendSlopePercent)
            return;

        if (trendStrengthPercent < _minTrendStrengthPercent)
            return;

        if (trendStrengthPercent > _maxTrendStrengthPercent)
            return;

        var atr = ComputeAtr(candles, index, _atrPeriod);
        var atrPercent = candle.Close > 0m ? atr / candle.Close * 100m : 0m;
        if (atrPercent > _maxAtrPercent)
            return;

        var swingHigh = ComputeSwingHigh(candles, index - 1, _swingLookbackCandles);
        if (swingHigh <= 0m || swingHigh <= candle.Close)
            return;

        var pullbackDepthPercent = (swingHigh - candle.Low) / candle.Close * 100m;
        if (pullbackDepthPercent < _minPullbackDepthPercent || pullbackDepthPercent > _maxPullbackDepthPercent)
            return;

        var distanceToMediumMaPercent = Math.Abs(candle.Close - mediumMa.Value) / candle.Close * 100m;
        if (distanceToMediumMaPercent > _maxDistanceToMediumMaPercent)
            return;

        var pullbackLow = Math.Min(candle.Low, ComputePullbackLow(candles, index, 3));

        _pending = new PendingHtfPullbackV1(
            index,
            swingHigh,
            pullbackLow,
            mediumMa.Value,
            shortMa.Value,
            Math.Round(trendSlopePercent, 6),
            Math.Round(trendStrengthPercent, 6),
            Math.Round(pullbackDepthPercent, 6),
            Math.Round(atrPercent, 6),
            Math.Round(distanceToMediumMaPercent, 6));
    }

    private (decimal? MovePercent, HigherTimeframeMomentumPullbackV1TargetModelName TargetModel, string? CapReason)
        ResolveTargetMovePercent(
            PendingHtfPullbackV1 pending,
            IReadOnlyList<KlineCandle> candles,
            int index,
            string interval,
            decimal entryPrice,
            decimal requiredGross,
            decimal minMoveFloor)
    {
        var swingTarget = pending.SwingHigh > entryPrice
            ? (pending.SwingHigh - entryPrice) / entryPrice * 100m
            : 0m;
        var atrTarget = pending.AtrPercent * _atrTargetMultiplier;
        var trendLeg = ComputeTrendLegPercent(candles, index, entryPrice) * _trendContinuationMultiplier;

        decimal? proposed = _targetModelName switch
        {
            HigherTimeframeMomentumPullbackV1TargetModelName.RecentSwingHighTarget => swingTarget,
            HigherTimeframeMomentumPullbackV1TargetModelName.AtrMultipleTarget => atrTarget,
            HigherTimeframeMomentumPullbackV1TargetModelName.TrendContinuationMeasuredMoveTarget => trendLeg,
            _ => Math.Max(swingTarget, Math.Max(atrTarget, trendLeg))
        };

        if (!proposed.HasValue || proposed.Value <= 0m)
            return (null, _targetModelName, "NoMove");

        proposed = Math.Max(proposed.Value, Math.Max(requiredGross, minMoveFloor));
        return (Math.Round(proposed.Value, 6), _targetModelName, null);
    }

    private decimal ResolveStopPrice(decimal entryPrice, PendingHtfPullbackV1 pending, decimal atrPercent)
    {
        return _stopMode switch
        {
            HigherTimeframeMomentumPullbackV1StopModeName.AtrStop =>
                entryPrice * (1m - (atrPercent * _atrStopMultiplier) / 100m),
            HigherTimeframeMomentumPullbackV1StopModeName.MediumMaInvalidation =>
                pending.MediumMa * (1m - _mediumMaInvalidationBufferPercent / 100m),
            _ => pending.PullbackLow * (1m - _stopBufferPercent / 100m)
        };
    }

    private HigherTimeframeMomentumPullbackV1Diagnostics BuildDiagnostics(
        PendingHtfPullbackV1 pending,
        KlineCandle candle,
        string interval)
        => new()
        {
            TrendSlopePercent = pending.TrendSlopePercent,
            TrendStrengthPercent = pending.TrendStrengthPercent,
            PullbackDepthPercent = pending.PullbackDepthPercent,
            DistanceToMediumMaPercent = pending.DistanceToMediumMaPercent,
            DistanceToShortMaPercent = pending.ShortMa > 0m
                ? Math.Round(Math.Abs(candle.Close - pending.ShortMa) / candle.Close * 100m, 6)
                : null,
            AtrPercent = pending.AtrPercent,
            ReclaimConfirmed = false,
            Interval = interval
        };

    private static decimal ComputeSwingHigh(IReadOnlyList<KlineCandle> candles, int endIndex, int lookback)
    {
        var start = Math.Max(0, endIndex - lookback + 1);
        decimal high = 0m;
        for (var i = start; i <= endIndex; i++)
            high = Math.Max(high, candles[i].High);
        return high;
    }

    private static decimal ComputePullbackLow(IReadOnlyList<KlineCandle> candles, int endIndex, int lookback)
    {
        var start = Math.Max(0, endIndex - lookback + 1);
        decimal low = decimal.MaxValue;
        for (var i = start; i <= endIndex; i++)
            low = Math.Min(low, candles[i].Low);
        return low == decimal.MaxValue ? 0m : low;
    }

    private static decimal ComputeTrendLegPercent(IReadOnlyList<KlineCandle> candles, int index, decimal entryPrice)
    {
        if (index < 6 || entryPrice <= 0m)
            return 0m;
        var start = Math.Max(0, index - 6);
        var low = candles[start].Low;
        for (var i = start; i <= index; i++)
            low = Math.Min(low, candles[i].Low);
        return low > 0m ? (entryPrice - low) / entryPrice * 100m : 0m;
    }

    private static decimal? ComputeSma(IReadOnlyList<KlineCandle> candles, int index, int period)
    {
        if (index < period - 1)
            return null;
        decimal sum = 0m;
        for (var i = index - period + 1; i <= index; i++)
            sum += candles[i].Close;
        return sum / period;
    }

    private static decimal ComputeAverageVolume(IReadOnlyList<KlineCandle> candles, int index, int period)
    {
        var start = Math.Max(0, index - period + 1);
        decimal sum = 0m;
        var count = 0;
        for (var i = start; i <= index; i++)
        {
            sum += candles[i].Volume;
            count++;
        }

        return count == 0 ? 0m : sum / count;
    }

    private static decimal ComputeAtr(IReadOnlyList<KlineCandle> candles, int index, int period)
    {
        if (index < period)
            return 0m;
        decimal sum = 0m;
        for (var i = index - period + 1; i <= index; i++)
        {
            var prevClose = candles[i - 1].Close;
            var tr = Math.Max(candles[i].High - candles[i].Low,
                Math.Max(Math.Abs(candles[i].High - prevClose), Math.Abs(candles[i].Low - prevClose)));
            sum += tr;
        }

        return sum / period;
    }

    private static decimal ResolveIntervalCap(IReadOnlyDictionary<string, decimal> map, string interval, decimal fallback)
        => map.TryGetValue(interval, out var value) ? value : fallback;

    private static Dictionary<string, decimal> ReadIntervalMap(
        IConfiguration configuration,
        string key,
        Dictionary<string, decimal> defaults)
    {
        var section = configuration.GetSection($"Backtest:HigherTimeframeMomentumPullbackV1:{key}");
        if (!section.Exists())
            return defaults;
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            if (decimal.TryParse(child.Value, out var parsed))
                result[child.Key] = parsed;
        }

        return result.Count == 0 ? defaults : result;
    }

    private static HigherTimeframeMomentumPullbackV1TargetModelName ParseTargetModel(string? raw)
        => Enum.TryParse<HigherTimeframeMomentumPullbackV1TargetModelName>(raw, true, out var parsed)
            ? parsed
            : HigherTimeframeMomentumPullbackV1TargetModelName.HybridMinReasonableTarget;

    private static HigherTimeframeMomentumPullbackV1StopModeName ParseStopMode(string? raw)
        => Enum.TryParse<HigherTimeframeMomentumPullbackV1StopModeName>(raw, true, out var parsed)
            ? parsed
            : HigherTimeframeMomentumPullbackV1StopModeName.PullbackLow;
}

internal sealed record PendingHtfPullbackV1(
    int DetectedIndex,
    decimal SwingHigh,
    decimal PullbackLow,
    decimal MediumMa,
    decimal ShortMa,
    decimal TrendSlopePercent,
    decimal TrendStrengthPercent,
    decimal PullbackDepthPercent,
    decimal AtrPercent,
    decimal DistanceToMediumMaPercent);

public sealed record HigherTimeframeMomentumPullbackV1Diagnostics
{
    public string Interval { get; init; } = string.Empty;
    public decimal TrendSlopePercent { get; init; }
    public decimal TrendStrengthPercent { get; init; }
    public decimal PullbackDepthPercent { get; init; }
    public decimal? DistanceToMediumMaPercent { get; init; }
    public decimal? DistanceToShortMaPercent { get; init; }
    public bool ReclaimConfirmed { get; init; }
    public decimal? VolumeExpansionRatio { get; init; }
    public decimal AtrPercent { get; init; }
    public string? TargetModelName { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? RequiredGrossMovePercent { get; init; }
    public decimal? RequiredNetProfitPercent { get; init; }
    public decimal? EstimatedRoundTripCostPercent { get; init; }
    public decimal? MinExpectedMoveFloorPercent { get; init; }
    public decimal? StopDistancePercent { get; init; }
    public decimal? RewardRisk { get; init; }
    public decimal? Lock90DistancePercent { get; init; }
    public string? CapReason { get; init; }
    public string? RejectionReason { get; init; }
}

public enum HigherTimeframeMomentumPullbackV1StepKind
{
    NoAction,
    Entry,
    Blocked
}

public sealed record HigherTimeframeMomentumPullbackV1StepResult(
    HigherTimeframeMomentumPullbackV1StepKind Kind,
    StrategySignalResult? Signal,
    string? RejectionReason,
    HigherTimeframeMomentumPullbackV1Diagnostics Diagnostics)
{
    public static HigherTimeframeMomentumPullbackV1StepResult NoAction()
        => new(HigherTimeframeMomentumPullbackV1StepKind.NoAction, null, null, new HigherTimeframeMomentumPullbackV1Diagnostics());

    public static HigherTimeframeMomentumPullbackV1StepResult Entry(
        StrategySignalResult signal,
        HigherTimeframeMomentumPullbackV1Diagnostics diagnostics)
        => new(HigherTimeframeMomentumPullbackV1StepKind.Entry, signal, null, diagnostics);

    public static HigherTimeframeMomentumPullbackV1StepResult Blocked(
        string reason,
        HigherTimeframeMomentumPullbackV1Diagnostics diagnostics)
        => new(HigherTimeframeMomentumPullbackV1StepKind.Blocked, null, reason, diagnostics with { RejectionReason = reason });
}
