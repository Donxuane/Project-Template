using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public enum RangeExpansionV2TargetModelName
{
    RangeMeasuredMoveTarget,
    AtrExpansionTarget,
    BreakoutCandleExtensionTarget,
    HybridMaxReasonableTarget
}

public sealed class RangeExpansionBreakoutV2Model
{
    public const string GuardPrefix = "RangeExpansionV2:";
    public const string InsufficientHistory = GuardPrefix + "InsufficientHistory";
    public const string RangeTooNarrow = GuardPrefix + "RangeTooNarrow";
    public const string RangeTooWide = GuardPrefix + "RangeTooWide";
    public const string NoBreakoutClose = GuardPrefix + "NoBreakoutClose";
    public const string FollowThroughFailed = GuardPrefix + "FollowThroughFailed";
    public const string BreakoutBodyTooWeak = GuardPrefix + "BreakoutBodyTooWeak";
    public const string BreakoutCloseTooCloseToRangeHigh = GuardPrefix + "BreakoutCloseTooCloseToRangeHigh";
    public const string VolumeNotExpanded = GuardPrefix + "VolumeNotExpanded";
    public const string AtrNotExpanded = GuardPrefix + "AtrNotExpanded";
    public const string AntiChaseRangeHighExceeded = GuardPrefix + "AntiChaseRangeHighExceeded";
    public const string AntiChaseBreakoutDistanceExceeded = GuardPrefix + "AntiChaseBreakoutDistanceExceeded";
    public const string LockBelowRequiredGross = GuardPrefix + "LockBelowRequiredGross";
    public const string TargetModelProducedNoMove = GuardPrefix + "TargetModelProducedNoMove";
    public const string BreakoutCandleRangeTooWide = GuardPrefix + "BreakoutCandleRangeTooWide";
    public const string StructuralStopTooFarFromLock = GuardPrefix + "StructuralStopTooFarFromLock";
    public const string InflationProxyRejected = GuardPrefix + "InflationProxyRejected";
    public const string StopToLockRatioExceeded = GuardPrefix + "StopToLockRatioExceeded";
    public const string BreakoutQualityRejected = GuardPrefix + "BreakoutQualityRejected";
    public const string FailedBreakoutWeaknessRejected = GuardPrefix + "FailedBreakoutWeaknessRejected";

    private readonly bool _enabled;
    private readonly bool _experimentalFiltersEnabled;
    private readonly bool _separatorFiltersEnabled;
    private readonly bool _enableInflationFilter;
    private readonly bool _enableStopRiskFilter;
    private readonly bool _enableBreakoutQualityFilter;
    private readonly bool _enableFailedBreakoutFilter;
    private readonly decimal _maxExpectedMoveToRealizedMoveRatio;
    private readonly decimal _maxStopToLockRatio;
    private readonly decimal _separatorMinBreakoutBodyStrengthPercent;
    private readonly decimal _separatorMinBreakoutCloseAboveRangePercent;
    private readonly decimal _separatorMinAtrExpansionRatio;
    private readonly decimal _separatorMaxVolumeExpansionRatio;
    private readonly decimal _separatorMaxBreakoutCandleRangePercent;
    private readonly decimal _separatorMaxGivebackAtEntryPercent;
    private readonly decimal _separatorMinFollowThroughCloseStrengthPercent;
    private readonly decimal? _failedBreakoutMinBodyStrengthPercent;
    private readonly decimal? _expMinBreakoutCloseAboveRangePercent;
    private readonly decimal? _expMinBreakoutBodyStrengthPercent;
    private readonly decimal? _expMaxBreakoutCandleRangeToAtrRatio;
    private readonly decimal? _expMinVolumeExpansionRatio;
    private readonly decimal? _expMinAtrExpansionRatio;
    private readonly decimal? _expMaxStructuralStopToLock90Ratio;
    private readonly decimal? _expMinRangeWidthPercent;
    private readonly decimal? _expMaxRangeWidthPercent;
    private readonly int _rangeLookbackCandles;
    private readonly decimal _breakoutBufferPercent;
    private readonly decimal _minRangeWidthPercent;
    private readonly decimal _maxRangeWidthPercent;
    private readonly decimal _minBreakoutBodyStrengthPercent;
    private readonly decimal _minBreakoutCloseAboveRangePercent;
    private readonly decimal _minVolumeExpansionRatio;
    private readonly bool _requireVolumeExpansion;
    private readonly bool _requireAtrExpansion;
    private readonly decimal _atrExpansionRatio;
    private readonly int _atrPeriod;
    private readonly int _atrLongPeriod;
    private readonly decimal _maxDistanceFromRangeHighPercent;
    private readonly decimal _maxDistanceFromBreakoutPercent;
    private readonly RangeExpansionV2TargetModelName _targetModelName;
    private readonly decimal _rangeMeasuredMoveMultiplier;
    private readonly decimal _atrExpansionTargetMultiplier;
    private readonly decimal _breakoutCandleExtensionMultiplier;
    private readonly decimal _hybridMaxExpectedMovePercent;
    private readonly decimal _requiredNetProfitPercent;
    private readonly Dictionary<string, decimal> _maxExpectedMovePercentByInterval;

    private PendingBreakoutV2? _pending;

    public RangeExpansionBreakoutV2Model(IConfiguration configuration)
    {
        _enabled = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV2:Enabled") ?? false;
        _rangeLookbackCandles = Math.Max(3, configuration.GetValue<int?>("Backtest:RangeExpansionBreakoutV2:RangeLookbackCandles") ?? 12);
        _breakoutBufferPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:BreakoutBufferPercent") ?? 0.03m);
        _minRangeWidthPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:MinRangeWidthPercent") ?? 0.18m);
        _maxRangeWidthPercent = Math.Max(_minRangeWidthPercent, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:MaxRangeWidthPercent") ?? 0.55m);
        _minBreakoutBodyStrengthPercent = Math.Clamp(
            configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:MinBreakoutBodyStrengthPercent") ?? 55m,
            0m,
            100m);
        _minBreakoutCloseAboveRangePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:MinBreakoutCloseAboveRangePercent") ?? 0.05m);
        _minVolumeExpansionRatio = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:MinVolumeExpansionRatio") ?? 1.20m);
        _requireVolumeExpansion = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV2:RequireVolumeExpansion") ?? true;
        _requireAtrExpansion = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV2:RequireAtrExpansion") ?? true;
        _atrExpansionRatio = Math.Max(1m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:AtrExpansionRatio") ?? 1.05m);
        _atrPeriod = Math.Max(2, configuration.GetValue<int?>("Backtest:RangeExpansionBreakoutV2:AtrPeriod") ?? 14);
        _atrLongPeriod = Math.Max(_atrPeriod + 1, configuration.GetValue<int?>("Backtest:RangeExpansionBreakoutV2:AtrLongPeriod") ?? 28);
        _maxDistanceFromRangeHighPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:MaxDistanceFromRangeHighPercent") ?? 0.20m);
        _maxDistanceFromBreakoutPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:MaxDistanceFromBreakoutPercent") ?? 0.12m);
        _targetModelName = ParseTargetModel(configuration.GetValue<string?>("Backtest:RangeExpansionBreakoutV2:TargetModelName"));
        _rangeMeasuredMoveMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:RangeMeasuredMoveMultiplier") ?? 1.50m);
        _atrExpansionTargetMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:AtrExpansionTargetMultiplier") ?? 2.00m);
        _breakoutCandleExtensionMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:BreakoutCandleExtensionMultiplier") ?? 1.25m);
        _hybridMaxExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:HybridMaxExpectedMovePercent") ?? 1.20m);
        _requiredNetProfitPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:RequiredNetProfitPercent") ?? 0.10m);
        _maxExpectedMovePercentByInterval = ReadIntervalMap(configuration, "MaxExpectedMovePercentByInterval", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 1.20m,
            ["3m"] = 1.50m,
            ["5m"] = 1.80m
        });
        _experimentalFiltersEnabled = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:Enabled") ?? false;
        _expMinBreakoutCloseAboveRangePercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinBreakoutCloseAboveRangePercent");
        _expMinBreakoutBodyStrengthPercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinBreakoutBodyStrengthPercent");
        _expMaxBreakoutCandleRangeToAtrRatio = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MaxBreakoutCandleRangeToAtrRatio");
        _expMinVolumeExpansionRatio = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinVolumeExpansionRatio");
        _expMinAtrExpansionRatio = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinAtrExpansionRatio");
        _expMaxStructuralStopToLock90Ratio = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MaxStructuralStopToLock90Ratio");
        _expMinRangeWidthPercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinRangeWidthPercent");
        _expMaxRangeWidthPercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MaxRangeWidthPercent");
        _separatorFiltersEnabled = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:Enabled") ?? false;
        _enableInflationFilter = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableInflationFilter") ?? false;
        _enableStopRiskFilter = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableStopRiskFilter") ?? false;
        _enableBreakoutQualityFilter = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableBreakoutQualityFilter") ?? false;
        _enableFailedBreakoutFilter = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableFailedBreakoutFilter") ?? false;
        _maxExpectedMoveToRealizedMoveRatio = Math.Max(1m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MaxExpectedMoveToRealizedMoveRatio") ?? 1.40m);
        _maxStopToLockRatio = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MaxStopToLockRatio") ?? 2.75m);
        _separatorMinBreakoutBodyStrengthPercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MinBreakoutBodyStrengthPercent") ?? 84m;
        _separatorMinBreakoutCloseAboveRangePercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MinBreakoutCloseAboveRangePercent") ?? 0.07m;
        _separatorMinAtrExpansionRatio = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MinAtrExpansionRatio") ?? 1.12m;
        _separatorMaxVolumeExpansionRatio = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MaxVolumeExpansionRatio") ?? 2.15m;
        _separatorMaxBreakoutCandleRangePercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MaxBreakoutCandleRangePercent") ?? 0.13m;
        _separatorMaxGivebackAtEntryPercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MaxGivebackAtEntryPercent") ?? 35m;
        _separatorMinFollowThroughCloseStrengthPercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MinFollowThroughCloseStrengthPercent") ?? 55m;
        _failedBreakoutMinBodyStrengthPercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV2:SeparatorFilters:FailedBreakoutMinBreakoutBodyStrengthPercent");
    }

    public bool IsEnabled => _enabled;
    public int MinRequiredCandles => _rangeLookbackCandles + 2;
    public decimal RequiredNetProfitPercent => _requiredNetProfitPercent;

    public void Reset() => _pending = null;

    public RangeExpansionBreakoutV2StepResult ProcessCandle(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        TradingSymbol symbol,
        decimal? profitLockThresholdPercent,
        ExecutionCostSettings executionCosts,
        IConfiguration configuration)
    {
        if (!_enabled || index < 0 || index >= candles.Count)
            return RangeExpansionBreakoutV2StepResult.NoAction();

        if (_pending is not null)
        {
            var followThroughResult = EvaluateFollowThrough(
                candles, index, interval, profitLockThresholdPercent, executionCosts, configuration);
            if (followThroughResult is not null)
                return followThroughResult;
        }

        if (_pending is null)
            TryDetectBreakout(candles, index, interval);

        return RangeExpansionBreakoutV2StepResult.NoAction();
    }

    private RangeExpansionBreakoutV2StepResult? EvaluateFollowThrough(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        decimal? profitLockThresholdPercent,
        ExecutionCostSettings executionCosts,
        IConfiguration configuration)
    {
        var pending = _pending!;
        if (index != pending.BreakoutIndex + 1)
        {
            _pending = null;
            return RangeExpansionBreakoutV2StepResult.Blocked(
                FollowThroughFailed,
                BuildDiagnostics(pending, candles[index], interval) with
                {
                    BreakoutConfirmed = true,
                    FollowThroughConfirmed = false,
                    RejectionReason = FollowThroughFailed
                });
        }

        var breakoutCandle = candles[pending.BreakoutIndex];
        var followCandle = candles[index];
        var followThroughConfirmed = followCandle.Close > breakoutCandle.Close || followCandle.Close > breakoutCandle.High;

        var diagnosticsBase = BuildDiagnostics(pending, followCandle, interval) with
        {
            BreakoutConfirmed = true,
            FollowThroughConfirmed = followThroughConfirmed
        };

        _pending = null;

        if (!followThroughConfirmed)
        {
            return RangeExpansionBreakoutV2StepResult.Blocked(
                FollowThroughFailed,
                diagnosticsBase with { RejectionReason = FollowThroughFailed });
        }

        var entryPrice = followCandle.Close;
        var distanceFromRangeHighPercent = (entryPrice - pending.RangeHigh) / entryPrice * 100m;
        if (distanceFromRangeHighPercent > _maxDistanceFromRangeHighPercent)
        {
            return RangeExpansionBreakoutV2StepResult.Blocked(
                AntiChaseRangeHighExceeded,
                diagnosticsBase with
                {
                    DistanceFromBreakoutPercent = (entryPrice - pending.BreakoutClose) / entryPrice * 100m,
                    RejectionReason = AntiChaseRangeHighExceeded
                });
        }

        var distanceFromBreakoutPercent = (entryPrice - pending.BreakoutClose) / entryPrice * 100m;
        if (distanceFromBreakoutPercent > _maxDistanceFromBreakoutPercent)
        {
            return RangeExpansionBreakoutV2StepResult.Blocked(
                AntiChaseBreakoutDistanceExceeded,
                diagnosticsBase with
                {
                    DistanceFromBreakoutPercent = distanceFromBreakoutPercent,
                    RejectionReason = AntiChaseBreakoutDistanceExceeded
                });
        }

        var roundTrip = RangeExpansionCostModel.ComputeRoundTripCostPercent(executionCosts);
        var requiredGross = roundTrip + _requiredNetProfitPercent;

        var (expectedMovePercent, targetModelName, targetWasCapped, capReason) =
            ResolveTargetMovePercent(pending, candles, index, interval, entryPrice, requiredGross);

        diagnosticsBase = diagnosticsBase with
        {
            DistanceFromBreakoutPercent = distanceFromBreakoutPercent,
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
            return RangeExpansionBreakoutV2StepResult.Blocked(
                TargetModelProducedNoMove,
                diagnosticsBase with { RejectionReason = TargetModelProducedNoMove });
        }

        var lock90 = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 90m);
        var lock95 = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 95m);
        var lock98 = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 98m);
        var activeLockDistance = profitLockThresholdPercent switch
        {
            90m => lock90,
            95m => lock95,
            98m => lock98,
            _ => lock90
        };

        diagnosticsBase = diagnosticsBase with
        {
            Lock90DistancePercent = lock90,
            Lock95DistancePercent = lock95,
            Lock98DistancePercent = lock98,
            Lock90NetProfitPercent = lock90.HasValue ? lock90.Value - roundTrip : null
        };

        if (activeLockDistance is null or <= 0m || activeLockDistance.Value < requiredGross)
        {
            return RangeExpansionBreakoutV2StepResult.Blocked(
                LockBelowRequiredGross,
                diagnosticsBase with { RejectionReason = LockBelowRequiredGross });
        }

        if (_experimentalFiltersEnabled
            && _expMaxStructuralStopToLock90Ratio is > 0m
            && pending.RangeLow > 0m
            && entryPrice > 0m
            && lock90 is > 0m)
        {
            var structuralStopDistancePercent = (entryPrice - pending.RangeLow) / entryPrice * 100m;
            if (structuralStopDistancePercent / lock90.Value > _expMaxStructuralStopToLock90Ratio.Value)
            {
                return RangeExpansionBreakoutV2StepResult.Blocked(
                    StructuralStopTooFarFromLock,
                    diagnosticsBase with { RejectionReason = StructuralStopTooFarFromLock });
            }
        }

        var separatorMetrics = ComputeSeparatorMetrics(pending, breakoutCandle, followCandle, entryPrice, expectedMovePercent, lock90);
        diagnosticsBase = diagnosticsBase with
        {
            RealizedMoveProxyPercent = separatorMetrics.RealizedMoveProxyPercent,
            ExpectedMoveInflatedAtEntry = separatorMetrics.ExpectedMoveInflatedAtEntry,
            StopToLockRatio = separatorMetrics.StopToLockRatio,
            GivebackAtEntryPercent = separatorMetrics.GivebackAtEntryPercent,
            FollowThroughCloseStrengthPercent = separatorMetrics.FollowThroughCloseStrengthPercent
        };

        var separatorRejection = EvaluateSeparatorFilters(pending, separatorMetrics);
        if (separatorRejection is not null)
        {
            return RangeExpansionBreakoutV2StepResult.Blocked(
                separatorRejection,
                diagnosticsBase with { RejectionReason = separatorRejection });
        }

        var targetPrice = entryPrice + (entryPrice * expectedMovePercent.Value / 100m);
        var distanceToInvalidationPercent = pending.RangeLow > 0m
            ? (entryPrice - pending.RangeLow) / entryPrice * 100m
            : (decimal?)null;

        var signal = new StrategySignalResult
        {
            StrategyName = "RangeExpansionBreakoutV2",
            Signal = TradeSignal.Buy,
            Reason = "Range expansion V2 breakout with cost-aware larger-move target.",
            Confidence = 0.80m,
            ExpectedTargetPrice = targetPrice,
            ExpectedMovePercent = expectedMovePercent,
            ExpectedTargetSource = "RangeExpansionBreakoutV2." + targetModelName,
            BreakoutRangeHigh = pending.RangeHigh,
            BreakoutRangeLow = pending.RangeLow,
            BreakoutThresholdPrice = pending.BreakoutThresholdPrice,
            DistanceToInvalidationPercent = distanceToInvalidationPercent,
            ProjectionMode = targetModelName.ToString(),
            VolatilityRegime = pending.AtrExpansionRatio >= _atrExpansionRatio ? "Expanded" : "Normal"
        };

        return RangeExpansionBreakoutV2StepResult.Entry(
            signal,
            diagnosticsBase with { RejectionReason = null });
    }

    private void TryDetectBreakout(IReadOnlyList<KlineCandle> candles, int index, string interval)
    {
        if (index < _rangeLookbackCandles)
            return;

        var rangeStart = index - _rangeLookbackCandles;
        var rangeEnd = index - 1;
        if (rangeStart < 0 || rangeEnd < rangeStart)
            return;

        decimal rangeHigh = decimal.MinValue;
        decimal rangeLow = decimal.MaxValue;
        decimal rangeVolumeSum = 0m;
        for (var i = rangeStart; i <= rangeEnd; i++)
        {
            rangeHigh = Math.Max(rangeHigh, candles[i].High);
            rangeLow = Math.Min(rangeLow, candles[i].Low);
            rangeVolumeSum += candles[i].Volume;
        }

        if (rangeHigh <= 0m || rangeLow <= 0m || rangeHigh <= rangeLow)
            return;

        var mid = (rangeHigh + rangeLow) / 2m;
        var rangeWidthPercent = (rangeHigh - rangeLow) / mid * 100m;
        var minRangeWidth = ResolveMinRangeWidthPercent();
        var maxRangeWidth = ResolveMaxRangeWidthPercent();
        if (rangeWidthPercent < minRangeWidth)
            return;
        if (rangeWidthPercent > maxRangeWidth)
            return;

        var shortAtr = ComputeAtr(candles, index - 1, _atrPeriod);
        var longAtr = ComputeAtr(candles, index - 1, _atrLongPeriod);
        var atrPercent = candles[index - 1].Close > 0m ? shortAtr / candles[index - 1].Close * 100m : 0m;
        var atrExpansionRatio = shortAtr > 0m && longAtr > 0m ? shortAtr / longAtr : 1m;
        var minAtrExpansion = ResolveMinAtrExpansionRatio();
        if (_requireAtrExpansion && atrExpansionRatio < minAtrExpansion)
            return;

        var breakoutCandle = candles[index];
        var threshold = rangeHigh * (1m + _breakoutBufferPercent / 100m);
        if (breakoutCandle.Close <= threshold)
            return;

        var breakoutCloseAboveRangePercent = rangeHigh > 0m ? (breakoutCandle.Close - rangeHigh) / rangeHigh * 100m : 0m;
        if (breakoutCloseAboveRangePercent < ResolveMinBreakoutCloseAboveRangePercent())
            return;

        var candleRange = breakoutCandle.High - breakoutCandle.Low;
        var breakoutCandleRangePercent = breakoutCandle.Close > 0m ? candleRange / breakoutCandle.Close * 100m : 0m;
        if (_experimentalFiltersEnabled
            && _expMaxBreakoutCandleRangeToAtrRatio is > 0m
            && atrPercent > 0m
            && breakoutCandleRangePercent / atrPercent > _expMaxBreakoutCandleRangeToAtrRatio.Value)
            return;

        var body = Math.Abs(breakoutCandle.Close - breakoutCandle.Open);
        var breakoutBodyStrengthPercent = candleRange > 0m ? body / candleRange * 100m : 0m;
        if (breakoutBodyStrengthPercent < ResolveMinBreakoutBodyStrengthPercent())
            return;

        var rangeAvgVolume = rangeVolumeSum / (rangeEnd - rangeStart + 1);
        var volumeExpansionRatio = rangeAvgVolume > 0m ? breakoutCandle.Volume / rangeAvgVolume : 0m;
        if (_requireVolumeExpansion && volumeExpansionRatio < ResolveMinVolumeExpansionRatio())
            return;

        _pending = new PendingBreakoutV2(
            index,
            rangeHigh,
            rangeLow,
            rangeWidthPercent,
            breakoutCandle.Open,
            breakoutCandle.Close,
            breakoutCandle.High,
            threshold,
            atrPercent,
            atrExpansionRatio,
            breakoutCloseAboveRangePercent,
            breakoutBodyStrengthPercent,
            breakoutCandleRangePercent,
            volumeExpansionRatio);
    }

    private (decimal? MovePercent, RangeExpansionV2TargetModelName TargetModel, bool TargetWasCapped, string? CapReason)
        ResolveTargetMovePercent(
            PendingBreakoutV2 pending,
            IReadOnlyList<KlineCandle> candles,
            int index,
            string interval,
            decimal entryPrice,
            decimal requiredGross)
    {
        var intervalCap = ResolveIntervalCap(_maxExpectedMovePercentByInterval, interval, 1.20m);
        decimal? proposed = _targetModelName switch
        {
            RangeExpansionV2TargetModelName.RangeMeasuredMoveTarget =>
                pending.RangeWidthPercent * _rangeMeasuredMoveMultiplier,
            RangeExpansionV2TargetModelName.AtrExpansionTarget =>
                pending.AtrPercent * _atrExpansionTargetMultiplier,
            RangeExpansionV2TargetModelName.BreakoutCandleExtensionTarget =>
                pending.BreakoutCandleRangePercent * _breakoutCandleExtensionMultiplier,
            _ => Math.Max(
                pending.RangeWidthPercent * _rangeMeasuredMoveMultiplier,
                Math.Max(
                    pending.AtrPercent * _atrExpansionTargetMultiplier,
                    pending.BreakoutCandleRangePercent * _breakoutCandleExtensionMultiplier))
        };

        if (_targetModelName == RangeExpansionV2TargetModelName.HybridMaxReasonableTarget)
        {
            proposed = Math.Min(proposed ?? 0m, _hybridMaxExpectedMovePercent);
        }

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

    private RangeExpansionBreakoutV2Diagnostics BuildDiagnostics(
        PendingBreakoutV2 pending,
        KlineCandle candle,
        string interval)
        => new()
        {
            RangeHigh = pending.RangeHigh,
            RangeLow = pending.RangeLow,
            RangeWidthPercent = pending.RangeWidthPercent,
            BreakoutBufferPercent = _breakoutBufferPercent,
            BreakoutClose = pending.BreakoutClose,
            BreakoutConfirmed = true,
            AtrPercent = pending.AtrPercent,
            AtrExpansionRatio = pending.AtrExpansionRatio,
            BreakoutCloseAboveRangePercent = pending.BreakoutCloseAboveRangePercent,
            BreakoutBodyStrengthPercent = pending.BreakoutBodyStrengthPercent,
            BreakoutCandleRangePercent = pending.BreakoutCandleRangePercent,
            VolumeExpansionRatio = pending.VolumeExpansionRatio,
            CandidateAgeCandles = 1
        };

    private static RangeExpansionV2TargetModelName ParseTargetModel(string? raw)
        => Enum.TryParse<RangeExpansionV2TargetModelName>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : RangeExpansionV2TargetModelName.HybridMaxReasonableTarget;

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

    private decimal ResolveMinRangeWidthPercent()
        => _experimentalFiltersEnabled && _expMinRangeWidthPercent.HasValue
            ? _expMinRangeWidthPercent.Value
            : _minRangeWidthPercent;

    private decimal ResolveMaxRangeWidthPercent()
        => _experimentalFiltersEnabled && _expMaxRangeWidthPercent.HasValue
            ? Math.Max(ResolveMinRangeWidthPercent(), _expMaxRangeWidthPercent.Value)
            : _maxRangeWidthPercent;

    private decimal ResolveMinBreakoutCloseAboveRangePercent()
        => _experimentalFiltersEnabled && _expMinBreakoutCloseAboveRangePercent.HasValue
            ? _expMinBreakoutCloseAboveRangePercent.Value
            : _minBreakoutCloseAboveRangePercent;

    private decimal ResolveMinBreakoutBodyStrengthPercent()
        => _experimentalFiltersEnabled && _expMinBreakoutBodyStrengthPercent.HasValue
            ? _expMinBreakoutBodyStrengthPercent.Value
            : _minBreakoutBodyStrengthPercent;

    private decimal ResolveMinVolumeExpansionRatio()
        => _experimentalFiltersEnabled && _expMinVolumeExpansionRatio.HasValue
            ? _expMinVolumeExpansionRatio.Value
            : _minVolumeExpansionRatio;

    private decimal ResolveMinAtrExpansionRatio()
        => _experimentalFiltersEnabled && _expMinAtrExpansionRatio.HasValue
            ? _expMinAtrExpansionRatio.Value
            : _atrExpansionRatio;

    private sealed record SeparatorMetrics(
        decimal? RealizedMoveProxyPercent,
        bool ExpectedMoveInflatedAtEntry,
        decimal? StopToLockRatio,
        decimal? GivebackAtEntryPercent,
        decimal? FollowThroughCloseStrengthPercent);

    private SeparatorMetrics ComputeSeparatorMetrics(
        PendingBreakoutV2 pending,
        KlineCandle breakoutCandle,
        KlineCandle followCandle,
        decimal entryPrice,
        decimal? expectedMovePercent,
        decimal? lock90)
    {
        var measuredMoveProxy = pending.RangeWidthPercent * _rangeMeasuredMoveMultiplier;
        var atrProxy = pending.AtrPercent * _atrExpansionTargetMultiplier;
        var candleProxy = pending.BreakoutCandleRangePercent * _breakoutCandleExtensionMultiplier;
        var realizedMoveProxy = Math.Max(measuredMoveProxy, Math.Max(atrProxy, candleProxy));

        var inflatedAtEntry = expectedMovePercent.HasValue
            && realizedMoveProxy > 0m
            && expectedMovePercent.Value > realizedMoveProxy * _maxExpectedMoveToRealizedMoveRatio;

        decimal? stopToLock = null;
        if (pending.RangeLow > 0m && entryPrice > 0m && lock90 is > 0m)
        {
            var stopDistance = (entryPrice - pending.RangeLow) / entryPrice * 100m;
            stopToLock = Math.Round(stopDistance / lock90.Value, 6);
        }

        var bodyTop = Math.Max(breakoutCandle.Open, breakoutCandle.Close);
        var bodyBottom = Math.Min(breakoutCandle.Open, breakoutCandle.Close);
        var bodySize = bodyTop - bodyBottom;
        decimal? giveback = null;
        if (bodySize > 0m && entryPrice < bodyTop)
            giveback = Math.Round((bodyTop - entryPrice) / bodySize * 100m, 6);

        decimal? followStrength = null;
        var followRange = followCandle.High - followCandle.Low;
        if (followRange > 0m)
            followStrength = Math.Round((followCandle.Close - followCandle.Low) / followRange * 100m, 6);

        return new SeparatorMetrics(
            Math.Round(realizedMoveProxy, 6),
            inflatedAtEntry,
            stopToLock,
            giveback,
            followStrength);
    }

    private string? EvaluateSeparatorFilters(PendingBreakoutV2 pending, SeparatorMetrics metrics)
    {
        if (!_separatorFiltersEnabled)
            return null;

        if (_enableInflationFilter && metrics.ExpectedMoveInflatedAtEntry)
            return InflationProxyRejected;

        if (_enableStopRiskFilter
            && metrics.StopToLockRatio is > 0m
            && metrics.StopToLockRatio.Value > _maxStopToLockRatio)
            return StopToLockRatioExceeded;

        if (_enableBreakoutQualityFilter)
        {
            if (pending.BreakoutBodyStrengthPercent < _separatorMinBreakoutBodyStrengthPercent
                || pending.BreakoutCloseAboveRangePercent < _separatorMinBreakoutCloseAboveRangePercent
                || pending.AtrExpansionRatio < _separatorMinAtrExpansionRatio
                || pending.VolumeExpansionRatio > _separatorMaxVolumeExpansionRatio
                || pending.BreakoutCandleRangePercent > _separatorMaxBreakoutCandleRangePercent)
                return BreakoutQualityRejected;
        }

        if (_enableFailedBreakoutFilter)
        {
            if (_failedBreakoutMinBodyStrengthPercent.HasValue
                && pending.BreakoutBodyStrengthPercent < _failedBreakoutMinBodyStrengthPercent.Value)
                return FailedBreakoutWeaknessRejected;

            if (metrics.GivebackAtEntryPercent is > 0m
                && metrics.GivebackAtEntryPercent.Value > _separatorMaxGivebackAtEntryPercent)
                return FailedBreakoutWeaknessRejected;

            if (metrics.FollowThroughCloseStrengthPercent is >= 0m
                && metrics.FollowThroughCloseStrengthPercent.Value < _separatorMinFollowThroughCloseStrengthPercent)
                return FailedBreakoutWeaknessRejected;
        }

        return null;
    }

    private static Dictionary<string, decimal> ReadIntervalMap(
        IConfiguration configuration,
        string suffix,
        Dictionary<string, decimal> defaults)
    {
        foreach (var interval in new[] { "1m", "3m", "5m" })
        {
            var configured = configuration.GetValue<decimal?>($"Backtest:RangeExpansionBreakoutV2:{suffix}:{interval}");
            if (configured.HasValue)
                defaults[interval] = configured.Value;
        }

        return defaults;
    }

    private sealed record PendingBreakoutV2(
        int BreakoutIndex,
        decimal RangeHigh,
        decimal RangeLow,
        decimal RangeWidthPercent,
        decimal BreakoutOpen,
        decimal BreakoutClose,
        decimal BreakoutHigh,
        decimal BreakoutThresholdPrice,
        decimal AtrPercent,
        decimal AtrExpansionRatio,
        decimal BreakoutCloseAboveRangePercent,
        decimal BreakoutBodyStrengthPercent,
        decimal BreakoutCandleRangePercent,
        decimal VolumeExpansionRatio);
}

public sealed record RangeExpansionBreakoutV2Diagnostics
{
    public decimal RangeHigh { get; init; }
    public decimal RangeLow { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal BreakoutBufferPercent { get; init; }
    public decimal BreakoutClose { get; init; }
    public bool BreakoutConfirmed { get; init; }
    public bool FollowThroughConfirmed { get; init; }
    public decimal? DistanceFromBreakoutPercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal AtrExpansionRatio { get; init; }
    public decimal VolumeExpansionRatio { get; init; }
    public string? TargetModelName { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? Lock90DistancePercent { get; init; }
    public decimal? Lock95DistancePercent { get; init; }
    public decimal? Lock98DistancePercent { get; init; }
    public decimal? Lock90NetProfitPercent { get; init; }
    public decimal? EstimatedRoundTripCostPercent { get; init; }
    public decimal? RequiredNetProfitPercent { get; init; }
    public decimal? RequiredGrossMovePercent { get; init; }
    public bool TargetWasCapped { get; init; }
    public string? CapReason { get; init; }
    public string? RejectionReason { get; init; }
    public decimal? BreakoutCloseAboveRangePercent { get; init; }
    public decimal? BreakoutBodyStrengthPercent { get; init; }
    public decimal? BreakoutCandleRangePercent { get; init; }
    public int CandidateAgeCandles { get; init; } = 1;
    public decimal? RealizedMoveProxyPercent { get; init; }
    public bool ExpectedMoveInflatedAtEntry { get; init; }
    public decimal? StopToLockRatio { get; init; }
    public decimal? GivebackAtEntryPercent { get; init; }
    public decimal? FollowThroughCloseStrengthPercent { get; init; }
}

public sealed record RangeExpansionBreakoutV2StepResult
{
    public RangeExpansionV2StepKind Kind { get; init; }
    public StrategySignalResult Signal { get; init; } = new() { Signal = TradeSignal.Hold };
    public RangeExpansionBreakoutV2Diagnostics Diagnostics { get; init; } = new();
    public string? RejectionReason { get; init; }

    public static RangeExpansionBreakoutV2StepResult NoAction()
        => new() { Kind = RangeExpansionV2StepKind.NoAction };

    public static RangeExpansionBreakoutV2StepResult Entry(StrategySignalResult signal, RangeExpansionBreakoutV2Diagnostics diagnostics)
        => new() { Kind = RangeExpansionV2StepKind.Entry, Signal = signal, Diagnostics = diagnostics };

    public static RangeExpansionBreakoutV2StepResult Blocked(string reason, RangeExpansionBreakoutV2Diagnostics diagnostics)
        => new() { Kind = RangeExpansionV2StepKind.Blocked, RejectionReason = reason, Diagnostics = diagnostics with { RejectionReason = reason } };
}

public enum RangeExpansionV2StepKind
{
    NoAction,
    Blocked,
    Entry
}
