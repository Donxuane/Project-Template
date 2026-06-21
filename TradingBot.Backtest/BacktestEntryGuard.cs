using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;
using TradingBot.Shared.Configuration;

namespace TradingBot.Backtest;

public sealed class BacktestEntryGuard
{
    public const string ConfidenceBelowThreshold = "ConfidenceBelowThreshold";
    public const string MissingStrategyExpectedTarget = "MissingStrategyExpectedTarget";
    public const string ExpectedMoveBelowMinimum = "ExpectedMoveBelowMinimum";
    public const string ExpectedNetMoveBelowMinimum = "ExpectedNetMoveBelowMinimum";
    public const string MaxOpenPositionsReached = "MaxOpenPositionsReached";

    private readonly IConfiguration _configuration;
    private readonly ExecutionCostSettings _costSettings;
    private readonly bool _requireStrategyTarget;
    private readonly decimal _minExpectedMovePercent;
    private readonly decimal _minNetProfitPercent;
    private readonly int _maxOpenPositionsPerSymbol;
    private readonly decimal _roundTripCostPercent;
    private readonly ReachabilityConfidenceRelaxationSettings _relaxationSettings;

    public BacktestEntryGuard(IConfiguration configuration, ExecutionCostSettings costSettings)
    {
        _configuration = configuration;
        _costSettings = costSettings;
        _requireStrategyTarget = configuration.GetValue<bool?>("Trading:RequireStrategyExpectedTargetForSpotOpenLong") ?? false;
        _minExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:MinExpectedMovePercent") ?? 0m);
        _minNetProfitPercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:MinNetProfitPercent") ?? 0m);
        _maxOpenPositionsPerSymbol = Math.Max(1, configuration.GetValue<int?>("Trading:MaxOpenPositionsPerSymbol") ?? 1);
        _roundTripCostPercent = Math.Max(0m, (costSettings.FeeRatePercent * 2m) + costSettings.SpreadPercent + (costSettings.SlippagePercent * 2m));
        _relaxationSettings = ReachabilityConfidenceRelaxationSettings.FromConfiguration(configuration);
    }

    public EntryGuardDecision Evaluate(
        TradingSymbol symbol,
        StrategySignalResult signal,
        MarketSnapshot snapshot,
        bool hasOpenPositionForSymbol)
    {
        var minConfidence = ResolveMinConfidence(symbol, signal);
        if (signal.Confidence < minConfidence)
        {
            if (TryAllowReachabilityRelaxedConfidence(signal, minConfidence, out var relaxedDecision))
                return relaxedDecision;

            return Block(ConfidenceBelowThreshold, signal, minConfidence);
        }

        if (hasOpenPositionForSymbol && _maxOpenPositionsPerSymbol <= 1)
        {
            return Block(MaxOpenPositionsReached, signal, minConfidence);
        }

        var hasExpectedTarget = signal.ExpectedMovePercent.HasValue
                                && signal.ExpectedTargetPrice.HasValue
                                && !string.IsNullOrWhiteSpace(signal.ExpectedTargetSource);
        if (_requireStrategyTarget && !hasExpectedTarget)
        {
            return Block(MissingStrategyExpectedTarget, signal, minConfidence);
        }

        if (signal.ExpectedMovePercent.HasValue && signal.ExpectedMovePercent.Value < _minExpectedMovePercent)
        {
            return Block(ExpectedMoveBelowMinimum, signal, minConfidence);
        }

        var estimatedNetMovePercent = signal.ExpectedMovePercent.HasValue
            ? signal.ExpectedMovePercent.Value - _roundTripCostPercent
            : (decimal?)null;
        if (estimatedNetMovePercent.HasValue && estimatedNetMovePercent.Value < _minNetProfitPercent)
        {
            return Block(ExpectedNetMoveBelowMinimum, signal, minConfidence, estimatedNetMovePercent);
        }

        return new EntryGuardDecision
        {
            IsAllowed = true,
            Reason = string.Empty,
            ConfidenceThreshold = minConfidence,
            EstimatedRoundTripCostPercent = _roundTripCostPercent,
            EstimatedNetMovePercent = estimatedNetMovePercent
        };
    }

    private decimal ResolveMinConfidence(TradingSymbol symbol, StrategySignalResult signal)
    {
        var strategyName = string.IsNullOrWhiteSpace(signal.StrategyName)
            ? "MovingAverageTrendStrategy"
            : signal.StrategyName;

        var resolution = RuntimeTradingConfigResolver.ResolveConfidenceThreshold(
            _configuration,
            strategyName,
            TradeSignal.Buy.ToString(),
            TradingMode.Spot.ToString(),
            TradeExecutionIntent.OpenLong.ToString());

        return Math.Clamp(resolution.MinConfidence, 0m, 1m);
    }

    private bool TryAllowReachabilityRelaxedConfidence(
        StrategySignalResult signal,
        decimal minConfidence,
        out EntryGuardDecision decision)
    {
        decision = default!;
        if (!_relaxationSettings.Enabled || signal.Confidence < _relaxationSettings.RelaxedMinConfidence)
            return false;

        if (!signal.ExpectedMovePercent.HasValue || !signal.DistanceToInvalidationPercent.HasValue)
            return false;

        var lock90Distance = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(signal.ExpectedMovePercent, 90m);
        if (!lock90Distance.HasValue
            || lock90Distance.Value > _relaxationSettings.MaxLock90DistancePercent
            || signal.DistanceToInvalidationPercent.Value > _relaxationSettings.MaxDistanceToInvalidationPercent)
        {
            return false;
        }

        var estimatedNetMovePercent = signal.ExpectedMovePercent.Value - _roundTripCostPercent;
        decision = new EntryGuardDecision
        {
            IsAllowed = true,
            Reason = string.Empty,
            ConfidenceThreshold = minConfidence,
            EstimatedRoundTripCostPercent = _roundTripCostPercent,
            EstimatedNetMovePercent = estimatedNetMovePercent
        };
        return true;
    }

    private EntryGuardDecision Block(string reason, StrategySignalResult signal, decimal minConfidence, decimal? estimatedNetMovePercent = null)
    {
        return new EntryGuardDecision
        {
            IsAllowed = false,
            Reason = reason,
            ConfidenceThreshold = minConfidence,
            EstimatedRoundTripCostPercent = _roundTripCostPercent,
            EstimatedNetMovePercent = estimatedNetMovePercent ?? (signal.ExpectedMovePercent.HasValue ? signal.ExpectedMovePercent.Value - _roundTripCostPercent : null)
        };
    }
}

public sealed record EntryGuardDecision
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal ConfidenceThreshold { get; init; }
    public decimal EstimatedRoundTripCostPercent { get; init; }
    public decimal? EstimatedNetMovePercent { get; init; }
}
