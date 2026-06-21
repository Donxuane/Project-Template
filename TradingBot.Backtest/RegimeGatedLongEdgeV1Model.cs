using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public enum RegimeGatedLongEdgeV1GateName
{
    ElevatedVolMarketReturnGate,
    WideRangeNearLowGate,
    Bnb30mUnconditionalPositiveBaseline,
    ElevatedVolMarketReturnWithBtcFavorableGate,
    WideRangeNearLowWithBtcFavorableGate
}

public enum RegimeGatedLongEdgeV1EntryConfirmationMode
{
    NextClose,
    NextOpen,
    CloseAbovePrevHigh,
    CloseAboveShortMa,
    BullishNearLow
}

public sealed class RegimeGatedLongEdgeV1Model
{
    public const string GuardPrefix = "RegimeGatedLongEdgeV1:";
    public const string WrongSymbolInterval = GuardPrefix + "WrongSymbolInterval";
    public const string GateNotTriggered = GuardPrefix + "GateNotTriggered";
    public const string ConfirmationFailed = GuardPrefix + "ConfirmationFailed";
    public const string PendingConfirmation = GuardPrefix + "PendingConfirmation";

    private readonly bool _enabled;
    private readonly RegimeGatedLongEdgeV1GateName _gateName;
    private readonly RegimeGatedLongEdgeV1EntryConfirmationMode _entryConfirmationMode;
    private readonly decimal _targetPercent;
    private readonly decimal _stopPercent;
    private readonly decimal _rangeWidthMinPercent;
    private readonly decimal _rangeWidthMaxPercent;
    private readonly decimal _distanceFromRecentLowMinPercent;
    private readonly decimal _distanceFromRecentLowMaxPercent;
    private readonly decimal? _minMarketWideReturnProxyPercent;
    private readonly decimal? _maxMarketWideReturnProxyPercent;
    private readonly bool _requireBtcFavorable;
    private readonly bool _requireBtcAboveMediumMa;
    private readonly decimal? _minBtcReturn30mPercent;
    private readonly HashSet<string> _allowedBtcTrendRegimes;
    private readonly HashSet<string> _allowedBtcMarketDirectionBuckets;
    private readonly int _shortMaPeriod;

    private PendingRegimeGateV1? _pending;

    public RegimeGatedLongEdgeV1Model(IConfiguration configuration)
    {
        _enabled = configuration.GetValue<bool?>("Backtest:RegimeGatedLongEdgeV1:Enabled") ?? false;
        _gateName = ParseGateName(configuration.GetValue<string?>("Backtest:RegimeGatedLongEdgeV1:RegimeGateName"));
        _entryConfirmationMode = ParseEntryConfirmation(configuration.GetValue<string?>("Backtest:RegimeGatedLongEdgeV1:EntryConfirmationMode"));
        _targetPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RegimeGatedLongEdgeV1:TargetPercent") ?? 0.50m);
        _stopPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RegimeGatedLongEdgeV1:StopPercent") ?? 0.50m);
        _rangeWidthMinPercent = configuration.GetValue<decimal?>("Backtest:RegimeGatedLongEdgeV1:RangeWidthMinPercent") ?? 0.8608m;
        _rangeWidthMaxPercent = configuration.GetValue<decimal?>("Backtest:RegimeGatedLongEdgeV1:RangeWidthMaxPercent") ?? 10.5309m;
        _distanceFromRecentLowMinPercent = configuration.GetValue<decimal?>("Backtest:RegimeGatedLongEdgeV1:DistanceFromRecentLowMinPercent") ?? 0m;
        _distanceFromRecentLowMaxPercent = configuration.GetValue<decimal?>("Backtest:RegimeGatedLongEdgeV1:DistanceFromRecentLowMaxPercent") ?? 0.1671m;
        _minMarketWideReturnProxyPercent = configuration.GetValue<decimal?>("Backtest:RegimeGatedLongEdgeV1:MinMarketWideReturnProxyPercent");
        _maxMarketWideReturnProxyPercent = configuration.GetValue<decimal?>("Backtest:RegimeGatedLongEdgeV1:MaxMarketWideReturnProxyPercent");
        _shortMaPeriod = Math.Max(3, configuration.GetValue<int?>("Backtest:RegimeGatedLongEdgeV1:ShortMaPeriod") ?? 8);
        _requireBtcFavorable = configuration.GetValue<bool?>("Backtest:RegimeGatedLongEdgeV1:RequireBtcFavorable") ?? false;
        _requireBtcAboveMediumMa = configuration.GetValue<bool?>("Backtest:RegimeGatedLongEdgeV1:RequireBtcAboveMediumMa") ?? true;
        _minBtcReturn30mPercent = configuration.GetValue<decimal?>("Backtest:RegimeGatedLongEdgeV1:MinBtcReturn30mPercent") ?? 0m;
        _allowedBtcTrendRegimes = ParseCsvSet(configuration.GetValue<string?>("Backtest:RegimeGatedLongEdgeV1:AllowedBtcTrendRegimes") ?? "BtcUp,BtcFlat");
        _allowedBtcMarketDirectionBuckets = ParseCsvSet(configuration.GetValue<string?>("Backtest:RegimeGatedLongEdgeV1:AllowedBtcMarketDirectionBuckets") ?? "RiskOn,Neutral");
    }

    public bool IsEnabled => _enabled;
    public int MinRequiredCandles => MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2;
    public string GateName => _gateName.ToString();
    public decimal TargetPercent => _targetPercent;
    public decimal StopPercent => _stopPercent;
    public RegimeGatedLongEdgeV1EntryConfirmationMode EntryConfirmationMode => _entryConfirmationMode;

    public void Reset() => _pending = null;

    public RegimeGatedLongEdgeV1StepResult ProcessCandle(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        TradingSymbol symbol,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext)
    {
        if (!_enabled || index < 0 || index >= candles.Count)
            return RegimeGatedLongEdgeV1StepResult.NoAction();

        if (_pending is not null)
        {
            var confirmResult = TryConfirmEntry(candles, index, interval, symbol, btcContext, marketWideContext);
            if (confirmResult is not null)
                return confirmResult;
        }

        if (index < MinRequiredCandles)
            return RegimeGatedLongEdgeV1StepResult.NoAction();

        var candle = candles[index];
        var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
            candles, index, btcContext, marketWideContext, candle.OpenTimeUtc);

        if (!PassesGate(symbol, interval, features))
            return RegimeGatedLongEdgeV1StepResult.NoAction();

        _pending = new PendingRegimeGateV1(index, features);
        return RegimeGatedLongEdgeV1StepResult.NoAction();
    }

    private RegimeGatedLongEdgeV1StepResult? TryConfirmEntry(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        TradingSymbol symbol,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext)
    {
        var pending = _pending!;
        if (index <= pending.TriggeredIndex)
            return null;

        if (index > pending.TriggeredIndex + 1)
        {
            _pending = null;
            var expiredDiagnostics = BuildDiagnostics(pending.Features, interval, _targetPercent, _stopPercent);
            return RegimeGatedLongEdgeV1StepResult.Blocked(ConfirmationFailed, expiredDiagnostics);
        }

        var entryCandle = candles[index];
        var prevCandle = candles[pending.TriggeredIndex];
        var entryFeatures = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
            candles, index, btcContext, marketWideContext, entryCandle.OpenTimeUtc);
        var diagnostics = BuildDiagnostics(entryFeatures, interval, _targetPercent, _stopPercent);

        if (!PassesConfirmation(candles, index, pending.TriggeredIndex, entryFeatures))
        {
            _pending = null;
            return RegimeGatedLongEdgeV1StepResult.Blocked(
                ConfirmationFailed,
                diagnostics with { RejectionReason = ConfirmationFailed });
        }

        _pending = null;
        var entryPrice = _entryConfirmationMode == RegimeGatedLongEdgeV1EntryConfirmationMode.NextOpen
            ? entryCandle.Open
            : entryCandle.Close;
        if (entryPrice <= 0m)
            return RegimeGatedLongEdgeV1StepResult.Blocked(
                ConfirmationFailed,
                diagnostics with { RejectionReason = ConfirmationFailed });

        var targetPrice = entryPrice * (1m + _targetPercent / 100m);
        var stopPrice = entryPrice * (1m - _stopPercent / 100m);
        var signal = new StrategySignalResult
        {
            StrategyName = "RegimeGatedLongEdgeV1",
            Signal = TradeSignal.Buy,
            Reason = $"Regime-gated long entry via {_gateName} with {_entryConfirmationMode} confirmation.",
            Confidence = 0.75m,
            ExpectedTargetPrice = targetPrice,
            ExpectedMovePercent = _targetPercent,
            ExpectedTargetSource = $"RegimeGatedLongEdgeV1.{_gateName}",
            BreakoutRangeHigh = entryPrice,
            BreakoutRangeLow = stopPrice,
            DistanceToInvalidationPercent = _stopPercent,
            TrendStrengthPercent = entryFeatures.TrendStrengthPercent,
            ShortMaSlopePercent = entryFeatures.TrendSlopePercent,
            VolatilityRegime = entryFeatures.VolatilityRegime,
            ProjectionMode = $"{_gateName}|t{_targetPercent:F2}|s{_stopPercent:F2}"
        };

        return RegimeGatedLongEdgeV1StepResult.Entry(signal, diagnostics with { RejectionReason = null });
    }

    private bool PassesGate(TradingSymbol symbol, string interval, MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features)
        => _gateName switch
        {
            RegimeGatedLongEdgeV1GateName.Bnb30mUnconditionalPositiveBaseline =>
                symbol == TradingSymbol.BNBUSDT && string.Equals(interval, "30m", StringComparison.OrdinalIgnoreCase),
            RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnGate =>
                string.Equals(features.VolatilityRegime, "Elevated", StringComparison.OrdinalIgnoreCase)
                && PassesMarketWideProxy(features.MarketWideReturnProxyPercent),
            RegimeGatedLongEdgeV1GateName.WideRangeNearLowGate =>
                features.RangeWidthPercent >= _rangeWidthMinPercent
                && features.RangeWidthPercent <= _rangeWidthMaxPercent
                && features.DistanceFromRecentLowPercent >= _distanceFromRecentLowMinPercent
                && features.DistanceFromRecentLowPercent <= _distanceFromRecentLowMaxPercent,
            RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnWithBtcFavorableGate =>
                string.Equals(features.VolatilityRegime, "Elevated", StringComparison.OrdinalIgnoreCase)
                && PassesMarketWideProxy(features.MarketWideReturnProxyPercent)
                && PassesBtcFavorable(features),
            RegimeGatedLongEdgeV1GateName.WideRangeNearLowWithBtcFavorableGate =>
                features.RangeWidthPercent >= _rangeWidthMinPercent
                && features.RangeWidthPercent <= _rangeWidthMaxPercent
                && features.DistanceFromRecentLowPercent >= _distanceFromRecentLowMinPercent
                && features.DistanceFromRecentLowPercent <= _distanceFromRecentLowMaxPercent
                && PassesBtcFavorable(features),
            _ => false
        };

    private bool PassesBtcFavorable(MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features)
    {
        if (!_requireBtcFavorable && _gateName is not (
            RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnWithBtcFavorableGate
            or RegimeGatedLongEdgeV1GateName.WideRangeNearLowWithBtcFavorableGate))
        {
            return true;
        }

        if (!features.BtcReturn60mPercent.HasValue)
            return false;
        if (_requireBtcAboveMediumMa && features.BtcAboveMediumMa != true)
            return false;
        if (features.BtcTrendRegime is null || !_allowedBtcTrendRegimes.Contains(features.BtcTrendRegime))
            return false;
        if (features.BtcMarketDirectionBucket is null || !_allowedBtcMarketDirectionBuckets.Contains(features.BtcMarketDirectionBucket))
            return false;
        if (_minBtcReturn30mPercent.HasValue
            && (!features.BtcReturn30mPercent.HasValue || features.BtcReturn30mPercent.Value < _minBtcReturn30mPercent.Value))
        {
            return false;
        }

        return true;
    }

    private bool PassesMarketWideProxy(decimal? proxy)
    {
        if (!proxy.HasValue)
            return false;
        if (_minMarketWideReturnProxyPercent.HasValue && proxy.Value < _minMarketWideReturnProxyPercent.Value)
            return false;
        if (_maxMarketWideReturnProxyPercent.HasValue && proxy.Value > _maxMarketWideReturnProxyPercent.Value)
            return false;
        return true;
    }

    private bool PassesConfirmation(
        IReadOnlyList<KlineCandle> candles,
        int index,
        int triggerIndex,
        MarketRegimeForwardEdgeScanner.RegimeCandleFeatures entryFeatures)
    {
        var entryCandle = candles[index];
        var triggerCandle = candles[triggerIndex];
        return _entryConfirmationMode switch
        {
            RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose => true,
            RegimeGatedLongEdgeV1EntryConfirmationMode.NextOpen => true,
            RegimeGatedLongEdgeV1EntryConfirmationMode.CloseAbovePrevHigh =>
                entryCandle.Close > triggerCandle.High,
            RegimeGatedLongEdgeV1EntryConfirmationMode.CloseAboveShortMa =>
                ComputeSma(candles, index, _shortMaPeriod) is decimal shortMa && entryCandle.Close > shortMa,
            RegimeGatedLongEdgeV1EntryConfirmationMode.BullishNearLow =>
                entryCandle.Close > entryCandle.Open
                && entryFeatures.DistanceFromRecentLowPercent >= _distanceFromRecentLowMinPercent
                && entryFeatures.DistanceFromRecentLowPercent <= _distanceFromRecentLowMaxPercent,
            _ => false
        };
    }

    private static RegimeGatedLongEdgeV1Diagnostics BuildDiagnostics(
        MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features,
        string interval,
        decimal targetPercent,
        decimal stopPercent)
        => new()
        {
            RuleName = string.Empty,
            Interval = interval,
            VolatilityRegime = features.VolatilityRegime,
            MarketWideReturnProxyPercent = features.MarketWideReturnProxyPercent,
            RangeWidthPercent = features.RangeWidthPercent,
            DistanceFromRecentLowPercent = features.DistanceFromRecentLowPercent,
            DistanceFromRecentHighPercent = features.DistanceFromRecentHighPercent,
            TrendSlopePercent = features.TrendSlopePercent,
            AtrPercent = features.AtrPercent,
            VolumeExpansionRatio = features.VolumeExpansionRatio,
            TargetPercent = targetPercent,
            StopPercent = stopPercent
        };

    private static decimal? ComputeSma(IReadOnlyList<KlineCandle> candles, int index, int period)
    {
        if (index < period - 1)
            return null;
        decimal sum = 0m;
        for (var i = index - period + 1; i <= index; i++)
            sum += candles[i].Close;
        return sum / period;
    }

    private static RegimeGatedLongEdgeV1GateName ParseGateName(string? raw)
        => Enum.TryParse<RegimeGatedLongEdgeV1GateName>(raw, true, out var parsed)
            ? parsed
            : RegimeGatedLongEdgeV1GateName.Bnb30mUnconditionalPositiveBaseline;

    private static RegimeGatedLongEdgeV1EntryConfirmationMode ParseEntryConfirmation(string? raw)
        => Enum.TryParse<RegimeGatedLongEdgeV1EntryConfirmationMode>(raw, true, out var parsed)
            ? parsed
            : RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose;

    private static HashSet<string> ParseCsvSet(string? raw)
        => (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

internal sealed record PendingRegimeGateV1(int TriggeredIndex, MarketRegimeForwardEdgeScanner.RegimeCandleFeatures Features);

public sealed record RegimeGatedLongEdgeV1Diagnostics
{
    public string RuleName { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string VolatilityRegime { get; init; } = string.Empty;
    public decimal? MarketWideReturnProxyPercent { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal DistanceFromRecentLowPercent { get; init; }
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal VolumeExpansionRatio { get; init; }
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public decimal? TimeStopHours { get; init; }
    public string? RejectionReason { get; init; }
}

public enum RegimeGatedLongEdgeV1StepKind
{
    NoAction,
    Entry,
    Blocked
}

public sealed record RegimeGatedLongEdgeV1StepResult(
    RegimeGatedLongEdgeV1StepKind Kind,
    StrategySignalResult? Signal,
    string? RejectionReason,
    RegimeGatedLongEdgeV1Diagnostics Diagnostics)
{
    public static RegimeGatedLongEdgeV1StepResult NoAction()
        => new(RegimeGatedLongEdgeV1StepKind.NoAction, null, null, new RegimeGatedLongEdgeV1Diagnostics());

    public static RegimeGatedLongEdgeV1StepResult Entry(StrategySignalResult signal, RegimeGatedLongEdgeV1Diagnostics diagnostics)
        => new(RegimeGatedLongEdgeV1StepKind.Entry, signal, null, diagnostics);

    public static RegimeGatedLongEdgeV1StepResult Blocked(string reason, RegimeGatedLongEdgeV1Diagnostics diagnostics)
        => new(RegimeGatedLongEdgeV1StepKind.Blocked, null, reason, diagnostics with { RejectionReason = reason });
}
