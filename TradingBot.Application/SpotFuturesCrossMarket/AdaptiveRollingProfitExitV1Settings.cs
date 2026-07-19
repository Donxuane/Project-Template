using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Application.SpotFuturesCrossMarket;

public sealed record AdaptiveRollingProfitExitV1Settings
{
    public const string SectionName = "AdaptiveRollingProfitExitV1";
    public const string FeatureName = "AdaptiveRollingProfitExitV1";

    public bool Enabled { get; init; }
    public string ApplicationId { get; init; } = "TradingBot";
    public string AccountKey { get; init; } = "futures-testnet";
    public int EvaluationIntervalMs { get; init; } = 1000;
    public int MarketDataMaxAgeMs { get; init; } = 3000;
    public int StreamLatencyDegradedMs { get; init; } = 2000;
    public string WebSocketBaseUrl { get; init; } = "wss://stream.binancefuture.com/stream";
    public int WebSocketReconnectMinDelayMs { get; init; } = 1000;
    public int WebSocketReconnectMaxDelayMs { get; init; } = 30000;

    public decimal MinNetProfitUsdt { get; init; } = 0.35m;
    public decimal MinNetProfitBps { get; init; } = 4m;
    public int EligibilityDwellMs { get; init; } = 5000;
    public int EligibilityConsecutiveObservations { get; init; } = 3;
    public decimal MinCloseNetProfitUsdt { get; init; } = 0.10m;
    public decimal MinCloseNetProfitBps { get; init; } = 1m;
    public decimal GivebackAbsoluteUsdt { get; init; } = 0.25m;
    public decimal GivebackPercent { get; init; } = 35m;
    public int ExitConfirmationObservations { get; init; } = 2;
    public int StateHysteresisMs { get; init; } = 2500;

    public decimal RideTrendScoreMin { get; init; } = 20m;
    public decimal WeakTrendScoreMax { get; init; } = 5m;
    public decimal StrongReversalScoreMax { get; init; } = -35m;
    public decimal BookImbalanceWeight { get; init; } = 20m;
    public decimal AggressiveFlowWeight { get; init; } = 35m;
    public decimal VelocityWeight { get; init; } = 35m;
    public decimal MicropriceWeight { get; init; } = 10m;
    public int FlowWindowSeconds { get; init; } = 60;
    public int VelocityWindowSeconds { get; init; } = 60;
    public decimal VelocityReferenceBps { get; init; } = 10m;

    public decimal LatencyReserveBps { get; init; } = 2m;
    public decimal VolatilityReserveMultiplier { get; init; } = 0.50m;
    public decimal ConservativeFallbackTakerCommissionRate { get; init; } = 0.001m;
    public decimal ConservativeFallbackMakerCommissionRate { get; init; } = 0.001m;
    public int FeeRefreshIntervalMinutes { get; init; } = 360;
    public int FeeFreshMaxAgeMinutes { get; init; } = 720;
    public int FeeCacheTtlMinutes { get; init; } = 1440;
    public int FeeRefreshLockSeconds { get; init; } = 120;
    public int FeeRefreshMaxRetries { get; init; } = 3;
    public int FundingRefreshIntervalMinutes { get; init; } = 15;

    public bool EnableEarlyLossCut { get; init; } = true;
    public decimal EarlyLossCutGrossLossBps { get; init; } = 10m;
    public decimal EarlyLossCutMinGrossLossUsdt { get; init; } = 5m;
    public decimal EarlyLossCutTrendScoreMax { get; init; } = -25m;
    public int EarlyLossCutConfirmationObservations { get; init; } = 10;
    public int EarlyLossCutMinPositionAgeSeconds { get; init; } = 120;

    public bool EnableHardProfitLock { get; init; } = true;
    public decimal HardProfitLockTierUsdt { get; init; } = 1.50m;
    public decimal HardProfitLockTierBps { get; init; } = 12m;
    public decimal HardProfitLockGivebackUsdt { get; init; } = 0.50m;
    public decimal HardProfitLockGivebackPercent { get; init; } = 30m;

    public bool EnableDynamicTakeProfitStopLoss { get; init; }
    public int DynamicOrderUpdateCooldownSeconds { get; init; } = 60;
    public decimal TakeProfitExtensionStepBps { get; init; } = 15m;
    public decimal TakeProfitExtensionMaxBps { get; init; } = 60m;
    public decimal StopLossLockProfitBps { get; init; } = 2m;

    public int PeakCheckpointIntervalMs { get; init; } = 5000;
    public int RoutineEvaluationSampleMs { get; init; } = 5000;
    public int CloseLockSeconds { get; init; } = 120;
    public int OriginalMaxHoldMinutesFallback { get; init; } = 360;

    public static AdaptiveRollingProfitExitV1Settings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var settings = new AdaptiveRollingProfitExitV1Settings
        {
            Enabled = section.GetValue("Enabled", false),
            ApplicationId = CleanKey(section.GetValue<string>("ApplicationId"), "TradingBot"),
            AccountKey = CleanKey(section.GetValue<string>("AccountKey"), "futures-testnet"),
            EvaluationIntervalMs = Math.Max(250, section.GetValue("EvaluationIntervalMs", 1000)),
            MarketDataMaxAgeMs = Math.Max(500, section.GetValue("MarketDataMaxAgeMs", 3000)),
            StreamLatencyDegradedMs = Math.Max(250, section.GetValue("StreamLatencyDegradedMs", 2000)),
            WebSocketBaseUrl = section.GetValue<string>("WebSocketBaseUrl") is { Length: > 0 } ws
                ? ws
                : "wss://stream.binancefuture.com/stream",
            WebSocketReconnectMinDelayMs = Math.Max(250, section.GetValue("WebSocketReconnectMinDelayMs", 1000)),
            WebSocketReconnectMaxDelayMs = Math.Max(1000, section.GetValue("WebSocketReconnectMaxDelayMs", 30000)),
            MinNetProfitUsdt = Math.Max(0m, section.GetValue("MinNetProfitUsdt", 0.35m)),
            MinNetProfitBps = Math.Max(0m, section.GetValue("MinNetProfitBps", 4m)),
            EligibilityDwellMs = Math.Max(0, section.GetValue("EligibilityDwellMs", 5000)),
            EligibilityConsecutiveObservations = Math.Max(1, section.GetValue("EligibilityConsecutiveObservations", 3)),
            MinCloseNetProfitUsdt = Math.Max(0m, section.GetValue("MinCloseNetProfitUsdt", 0.10m)),
            MinCloseNetProfitBps = Math.Max(0m, section.GetValue("MinCloseNetProfitBps", 1m)),
            GivebackAbsoluteUsdt = Math.Max(0m, section.GetValue("GivebackAbsoluteUsdt", 0.25m)),
            GivebackPercent = Math.Clamp(section.GetValue("GivebackPercent", 35m), 0m, 100m),
            ExitConfirmationObservations = Math.Max(1, section.GetValue("ExitConfirmationObservations", 2)),
            StateHysteresisMs = Math.Max(0, section.GetValue("StateHysteresisMs", 2500)),
            RideTrendScoreMin = section.GetValue("RideTrendScoreMin", 20m),
            WeakTrendScoreMax = section.GetValue("WeakTrendScoreMax", 5m),
            StrongReversalScoreMax = section.GetValue("StrongReversalScoreMax", -35m),
            BookImbalanceWeight = Math.Max(0m, section.GetValue("BookImbalanceWeight", 20m)),
            AggressiveFlowWeight = Math.Max(0m, section.GetValue("AggressiveFlowWeight", 35m)),
            VelocityWeight = Math.Max(0m, section.GetValue("VelocityWeight", 35m)),
            MicropriceWeight = Math.Max(0m, section.GetValue("MicropriceWeight", 10m)),
            FlowWindowSeconds = Math.Max(1, section.GetValue("FlowWindowSeconds", 60)),
            VelocityWindowSeconds = Math.Max(1, section.GetValue("VelocityWindowSeconds", 60)),
            VelocityReferenceBps = Math.Max(0.1m, section.GetValue("VelocityReferenceBps", 10m)),
            LatencyReserveBps = Math.Max(0m, section.GetValue("LatencyReserveBps", 2m)),
            VolatilityReserveMultiplier = Math.Max(0m, section.GetValue("VolatilityReserveMultiplier", 0.50m)),
            ConservativeFallbackTakerCommissionRate = PositiveRate(section.GetValue("ConservativeFallbackTakerCommissionRate", 0.001m)),
            ConservativeFallbackMakerCommissionRate = PositiveRate(section.GetValue("ConservativeFallbackMakerCommissionRate", 0.001m)),
            FeeRefreshIntervalMinutes = Math.Max(30, section.GetValue("FeeRefreshIntervalMinutes", 360)),
            FeeFreshMaxAgeMinutes = Math.Max(30, section.GetValue("FeeFreshMaxAgeMinutes", 720)),
            FeeCacheTtlMinutes = Math.Max(60, section.GetValue("FeeCacheTtlMinutes", 1440)),
            FeeRefreshLockSeconds = Math.Max(10, section.GetValue("FeeRefreshLockSeconds", 120)),
            FeeRefreshMaxRetries = Math.Max(1, section.GetValue("FeeRefreshMaxRetries", 3)),
            FundingRefreshIntervalMinutes = Math.Max(1, section.GetValue("FundingRefreshIntervalMinutes", 15)),
            EnableEarlyLossCut = section.GetValue("EnableEarlyLossCut", true),
            EarlyLossCutGrossLossBps = Math.Max(1m, section.GetValue("EarlyLossCutGrossLossBps", 10m)),
            EarlyLossCutMinGrossLossUsdt = Math.Max(0m, section.GetValue("EarlyLossCutMinGrossLossUsdt", 5m)),
            EarlyLossCutTrendScoreMax = Math.Min(0m, section.GetValue("EarlyLossCutTrendScoreMax", -25m)),
            EarlyLossCutConfirmationObservations = Math.Max(1, section.GetValue("EarlyLossCutConfirmationObservations", 10)),
            EarlyLossCutMinPositionAgeSeconds = Math.Max(0, section.GetValue("EarlyLossCutMinPositionAgeSeconds", 120)),
            EnableHardProfitLock = section.GetValue("EnableHardProfitLock", true),
            HardProfitLockTierUsdt = Math.Max(0m, section.GetValue("HardProfitLockTierUsdt", 1.50m)),
            HardProfitLockTierBps = Math.Max(0m, section.GetValue("HardProfitLockTierBps", 12m)),
            HardProfitLockGivebackUsdt = Math.Max(0m, section.GetValue("HardProfitLockGivebackUsdt", 0.50m)),
            HardProfitLockGivebackPercent = Math.Clamp(section.GetValue("HardProfitLockGivebackPercent", 30m), 0m, 100m),
            EnableDynamicTakeProfitStopLoss = section.GetValue("EnableDynamicTakeProfitStopLoss", false),
            DynamicOrderUpdateCooldownSeconds = Math.Max(10, section.GetValue("DynamicOrderUpdateCooldownSeconds", 60)),
            TakeProfitExtensionStepBps = Math.Max(0m, section.GetValue("TakeProfitExtensionStepBps", 15m)),
            TakeProfitExtensionMaxBps = Math.Max(0m, section.GetValue("TakeProfitExtensionMaxBps", 60m)),
            StopLossLockProfitBps = Math.Max(0m, section.GetValue("StopLossLockProfitBps", 2m)),
            PeakCheckpointIntervalMs = Math.Max(500, section.GetValue("PeakCheckpointIntervalMs", 5000)),
            RoutineEvaluationSampleMs = Math.Max(500, section.GetValue("RoutineEvaluationSampleMs", 5000)),
            CloseLockSeconds = Math.Max(10, section.GetValue("CloseLockSeconds", 120)),
            OriginalMaxHoldMinutesFallback = Math.Max(1, section.GetValue("OriginalMaxHoldMinutesFallback", 360))
        };

        return settings;
    }

    public decimal EntryProfitArmThreshold(decimal entryNotional)
        => Math.Max(MinNetProfitUsdt, entryNotional * MinNetProfitBps / 10_000m);

    public decimal CloseProfitFloor(decimal entryNotional)
        => Math.Max(MinCloseNetProfitUsdt, entryNotional * MinCloseNetProfitBps / 10_000m);

    public decimal HardProfitTier(decimal entryNotional)
        => Math.Max(HardProfitLockTierUsdt, entryNotional * HardProfitLockTierBps / 10_000m);

    public decimal EarlyLossCutGrossFloor(decimal entryNotional)
        => Math.Max(EarlyLossCutMinGrossLossUsdt, entryNotional * EarlyLossCutGrossLossBps / 10_000m);

    private static string CleanKey(string? value, string fallback)
    {
        var chosen = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return string.Concat(chosen.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
    }

    private static decimal PositiveRate(decimal value) => value > 0m ? value : 0.001m;
}

public sealed record AdaptiveRollingProfitProjectedPnl(
    decimal EstimatedExecutablePrice,
    decimal GrossPnl,
    decimal ActualEntryCommissions,
    decimal EstimatedExitCommission,
    decimal Funding,
    decimal AdverseMoveReserve,
    decimal ProjectedNetPnl,
    decimal BreakEvenExecutablePrice);

public static class AdaptiveRollingProfitExitCalculator
{
    public static AdaptiveRollingProfitProjectedPnl Calculate(
        OrderSide side,
        decimal averageEntryPrice,
        decimal estimatedExecutablePrice,
        decimal remainingQuantity,
        decimal actualAllocatedEntryCommissions,
        decimal estimatedTakerCommissionRate,
        decimal signedFunding,
        decimal adverseMoveReserve)
    {
        if (averageEntryPrice <= 0m || estimatedExecutablePrice <= 0m || remainingQuantity <= 0m)
            return new AdaptiveRollingProfitProjectedPnl(0m, 0m, actualAllocatedEntryCommissions, 0m, signedFunding, adverseMoveReserve, 0m, 0m);

        var gross = side == OrderSide.BUY
            ? (estimatedExecutablePrice - averageEntryPrice) * remainingQuantity
            : (averageEntryPrice - estimatedExecutablePrice) * remainingQuantity;

        var exitFee = estimatedExecutablePrice * remainingQuantity * Math.Max(estimatedTakerCommissionRate, 0m);
        var net = gross + signedFunding - actualAllocatedEntryCommissions - exitFee - adverseMoveReserve;
        var breakEven = BreakEvenExecutablePrice(
            side,
            averageEntryPrice,
            remainingQuantity,
            actualAllocatedEntryCommissions,
            estimatedTakerCommissionRate,
            signedFunding,
            adverseMoveReserve);

        return new AdaptiveRollingProfitProjectedPnl(
            estimatedExecutablePrice,
            gross,
            actualAllocatedEntryCommissions,
            exitFee,
            signedFunding,
            adverseMoveReserve,
            net,
            breakEven);
    }

    public static decimal BreakEvenExecutablePrice(
        OrderSide side,
        decimal averageEntryPrice,
        decimal remainingQuantity,
        decimal actualAllocatedEntryCommissions,
        decimal estimatedTakerCommissionRate,
        decimal signedFunding,
        decimal adverseMoveReserve)
    {
        if (averageEntryPrice <= 0m || remainingQuantity <= 0m)
            return 0m;

        var feeRate = Math.Max(estimatedTakerCommissionRate, 0m);
        if (side == OrderSide.BUY)
        {
            var denominator = remainingQuantity * Math.Max(0.00000001m, 1m - feeRate);
            return (averageEntryPrice * remainingQuantity + actualAllocatedEntryCommissions + adverseMoveReserve - signedFunding) / denominator;
        }

        return (averageEntryPrice * remainingQuantity + signedFunding - actualAllocatedEntryCommissions - adverseMoveReserve)
               / (remainingQuantity * (1m + feeRate));
    }

    public static decimal ActualEntryCommissionFromOpenPosition(decimal realizedPnl)
        => realizedPnl < 0m ? -realizedPnl : 0m;
}
