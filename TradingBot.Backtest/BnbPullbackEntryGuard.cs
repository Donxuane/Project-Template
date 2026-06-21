using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public enum BnbPullbackGuardMode
{
    FieldsOnly,
    LockReachabilityOnly,
    Combined
}

public sealed class BnbPullbackEntryGuard
{
    public const string GuardPrefix = "BnbPullbackGuard:";
    public const string ExpectedMoveCapExceeded = GuardPrefix + "ExpectedMoveCapExceeded";
    public const string DistanceToInvalidationCapExceeded = GuardPrefix + "DistanceToInvalidationCapExceeded";
    public const string TrendStrengthCapExceeded = GuardPrefix + "TrendStrengthCapExceeded";
    public const string ResidualExpectedMoveCapExceeded = GuardPrefix + "ResidualExpectedMoveCapExceeded";
    public const string ResidualRewardRiskCapExceeded = GuardPrefix + "ResidualRewardRiskCapExceeded";
    public const string LockReachabilityExceeded = GuardPrefix + "LockReachabilityExceeded";
    public const string NotApplicableSymbol = GuardPrefix + "NotApplicableSymbol";

    private readonly bool _enabled;
    private readonly BnbPullbackGuardMode _mode;
    private readonly decimal _maxExpectedMovePercent;
    private readonly decimal _maxDistanceToInvalidationPercent;
    private readonly decimal _maxTrendStrengthPercent;
    private readonly decimal _maxResidualExpectedMovePercent;
    private readonly decimal _maxResidualRewardRisk;
    private readonly decimal _defaultMaxLockDistancePercent;
    private readonly Dictionary<string, decimal> _maxLockDistanceByInterval;

    public BnbPullbackEntryGuard(IConfiguration configuration)
    {
        _enabled = configuration.GetValue<bool?>("Backtest:BnbPullbackGuard:Enabled") ?? false;
        _mode = ParseMode(configuration.GetValue<string?>("Backtest:BnbPullbackGuard:Mode"));
        _maxExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbPullbackGuard:MaxExpectedMovePercent") ?? 0.50m);
        _maxDistanceToInvalidationPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbPullbackGuard:MaxDistanceToInvalidationPercent") ?? 0.40m);
        _maxTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbPullbackGuard:MaxTrendStrengthPercent") ?? 0.00090m);
        _maxResidualExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbPullbackGuard:MaxResidualExpectedMovePercent") ?? 0.45m);
        _maxResidualRewardRisk = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbPullbackGuard:MaxResidualRewardRisk") ?? 1.10m);
        _defaultMaxLockDistancePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:BnbPullbackGuard:MaxLockDistancePercent") ?? 0.40m);
        _maxLockDistanceByInterval = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var intervalSection = configuration.GetSection("Backtest:BnbPullbackGuard:MaxLockDistancePercentByInterval");
        foreach (var child in intervalSection.GetChildren())
        {
            if (decimal.TryParse(child.Value, out var parsed))
                _maxLockDistanceByInterval[child.Key] = Math.Max(0m, parsed);
        }
    }

    public bool IsEnabled => _enabled;

    public BnbPullbackGuardDecision Evaluate(
        TradingSymbol symbol,
        StrategySignalResult signal,
        PullbackV2Diagnostics? v2Diagnostics,
        string interval,
        decimal? profitLockThresholdPercent,
        bool isV2Path)
    {
        var diagnostics = BuildDiagnostics(signal, v2Diagnostics, interval, profitLockThresholdPercent, isV2Path);

        if (!_enabled)
        {
            return BnbPullbackGuardDecision.Allow(diagnostics with { BnbPullbackGuardEnabled = false });
        }

        if (symbol != TradingSymbol.BNBUSDT)
        {
            return BnbPullbackGuardDecision.Block(NotApplicableSymbol, diagnostics);
        }

        var expectedMoveCapRejected = false;
        var distanceCapRejected = false;
        var trendCapRejected = false;
        var residualExpectedMoveCapRejected = false;
        var residualRewardRiskCapRejected = false;
        var lockReachabilityRejected = false;
        string? primaryReason = null;

        if (_mode is BnbPullbackGuardMode.FieldsOnly or BnbPullbackGuardMode.Combined)
        {
            if (diagnostics.ExpectedMovePercent.HasValue && diagnostics.ExpectedMovePercent.Value > _maxExpectedMovePercent)
            {
                expectedMoveCapRejected = true;
                primaryReason ??= ExpectedMoveCapExceeded;
            }

            if (diagnostics.DistanceToInvalidationPercent.HasValue
                && diagnostics.DistanceToInvalidationPercent.Value > _maxDistanceToInvalidationPercent)
            {
                distanceCapRejected = true;
                primaryReason ??= DistanceToInvalidationCapExceeded;
            }

            if (diagnostics.TrendStrengthPercent.HasValue
                && diagnostics.TrendStrengthPercent.Value > _maxTrendStrengthPercent)
            {
                trendCapRejected = true;
                primaryReason ??= TrendStrengthCapExceeded;
            }

            if (isV2Path)
            {
                if (diagnostics.ResidualExpectedMovePercent.HasValue
                    && diagnostics.ResidualExpectedMovePercent.Value > _maxResidualExpectedMovePercent)
                {
                    residualExpectedMoveCapRejected = true;
                    primaryReason ??= ResidualExpectedMoveCapExceeded;
                }

                if (diagnostics.ResidualRewardRisk.HasValue
                    && diagnostics.ResidualRewardRisk.Value > _maxResidualRewardRisk)
                {
                    residualRewardRiskCapRejected = true;
                    primaryReason ??= ResidualRewardRiskCapExceeded;
                }
            }
        }

        if (_mode is BnbPullbackGuardMode.LockReachabilityOnly or BnbPullbackGuardMode.Combined)
        {
            if (diagnostics.LockDistancePercent.HasValue
                && diagnostics.MaxAllowedLockDistancePercent.HasValue
                && diagnostics.LockDistancePercent.Value > diagnostics.MaxAllowedLockDistancePercent.Value)
            {
                lockReachabilityRejected = true;
                primaryReason ??= LockReachabilityExceeded;
            }
        }

        diagnostics = diagnostics with
        {
            BnbPullbackGuardEnabled = true,
            ExpectedMoveCapRejected = expectedMoveCapRejected,
            DistanceToInvalidationCapRejected = distanceCapRejected,
            TrendStrengthCapRejected = trendCapRejected,
            ResidualExpectedMoveCapRejected = residualExpectedMoveCapRejected,
            ResidualRewardRiskCapRejected = residualRewardRiskCapRejected,
            LockReachabilityRejected = lockReachabilityRejected
        };

        if (primaryReason is not null)
            return BnbPullbackGuardDecision.Block(primaryReason, diagnostics);

        return BnbPullbackGuardDecision.Allow(diagnostics);
    }

    public static decimal? ComputeLockDistancePercent(decimal? movePercent, decimal? profitLockThresholdPercent)
    {
        if (!movePercent.HasValue || !profitLockThresholdPercent.HasValue)
            return null;

        return movePercent.Value * profitLockThresholdPercent.Value / 100m;
    }

    private BnbPullbackGuardDiagnostics BuildDiagnostics(
        StrategySignalResult signal,
        PullbackV2Diagnostics? v2Diagnostics,
        string interval,
        decimal? profitLockThresholdPercent,
        bool isV2Path)
    {
        var moveForLock = isV2Path && v2Diagnostics?.ResidualExpectedMovePercent.HasValue == true
            ? v2Diagnostics.ResidualExpectedMovePercent
            : signal.ExpectedMovePercent;

        var maxAllowedLockDistance = ResolveMaxLockDistancePercent(interval);
        var lockDistance = ComputeLockDistancePercent(moveForLock, profitLockThresholdPercent);

        return new BnbPullbackGuardDiagnostics
        {
            BnbPullbackGuardEnabled = _enabled,
            ExpectedMovePercent = signal.ExpectedMovePercent,
            DistanceToInvalidationPercent = signal.DistanceToInvalidationPercent,
            TrendStrengthPercent = signal.TrendStrengthPercent,
            ResidualExpectedMovePercent = v2Diagnostics?.ResidualExpectedMovePercent,
            ResidualRewardRisk = v2Diagnostics?.ResidualRewardRisk,
            LockDistancePercent = lockDistance,
            MaxAllowedLockDistancePercent = maxAllowedLockDistance,
            ConsecutiveBullishCandlesAtEntry = signal.ConsecutiveBullishTrendCandles,
            EntryNearRecentHigh = signal.EntryNearRecentHigh
        };
    }

    private decimal ResolveMaxLockDistancePercent(string interval)
    {
        if (_maxLockDistanceByInterval.TryGetValue(interval, out var configured))
            return configured;

        return _defaultMaxLockDistancePercent;
    }

    private static BnbPullbackGuardMode ParseMode(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "fieldsonly" or "fields-only" or "fields" => BnbPullbackGuardMode.FieldsOnly,
            "lockreachabilityonly" or "lock-reachability-only" or "lockreach" => BnbPullbackGuardMode.LockReachabilityOnly,
            _ => BnbPullbackGuardMode.Combined
        };
    }
}

public sealed record BnbPullbackGuardDiagnostics
{
    public bool BnbPullbackGuardEnabled { get; init; }
    public bool BnbPullbackGuardRejected { get; init; }
    public string? BnbPullbackGuardRejectedReason { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? DistanceToInvalidationPercent { get; init; }
    public decimal? TrendStrengthPercent { get; init; }
    public decimal? ResidualExpectedMovePercent { get; init; }
    public decimal? ResidualRewardRisk { get; init; }
    public decimal? LockDistancePercent { get; init; }
    public decimal? MaxAllowedLockDistancePercent { get; init; }
    public bool ExpectedMoveCapRejected { get; init; }
    public bool DistanceToInvalidationCapRejected { get; init; }
    public bool TrendStrengthCapRejected { get; init; }
    public bool ResidualExpectedMoveCapRejected { get; init; }
    public bool ResidualRewardRiskCapRejected { get; init; }
    public bool LockReachabilityRejected { get; init; }
    public int? ConsecutiveBullishCandlesAtEntry { get; init; }
    public bool? EntryNearRecentHigh { get; init; }
}

public sealed record BnbPullbackGuardDecision
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public BnbPullbackGuardDiagnostics Diagnostics { get; init; } = new();

    public static BnbPullbackGuardDecision Allow(BnbPullbackGuardDiagnostics diagnostics)
        => new() { IsAllowed = true, Diagnostics = diagnostics };

    public static BnbPullbackGuardDecision Block(string reason, BnbPullbackGuardDiagnostics diagnostics)
        => new()
        {
            IsAllowed = false,
            Reason = reason,
            Diagnostics = diagnostics with
            {
                BnbPullbackGuardRejected = true,
                BnbPullbackGuardRejectedReason = reason
            }
        };
}
