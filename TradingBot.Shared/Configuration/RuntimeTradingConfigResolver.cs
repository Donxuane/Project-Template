using Microsoft.Extensions.Configuration;

namespace TradingBot.Shared.Configuration;

public static class RuntimeTradingConfigResolver
{
    private const decimal DefaultMinConfidence = 0.70m;

    public static TradingRuntimeSettings ResolveTrading(IConfiguration configuration)
    {
        var modeText = configuration["Trading:Mode"];
        var mode = string.IsNullOrWhiteSpace(modeText) ? "Spot" : modeText;

        return new TradingRuntimeSettings(
            mode,
            configuration.GetValue<bool?>("Trading:AllowAddToPosition") ?? false,
            Math.Max(1, configuration.GetValue<int?>("Trading:MaxOpenPositionsPerSymbol") ?? 1),
            configuration.GetValue<bool?>("Trading:RequireStrategyExpectedTargetForSpotOpenLong") ?? false,
            configuration.GetValue<bool?>("Trading:UseBalanceBasedSizing") ?? false,
            Math.Clamp(configuration.GetValue<decimal?>("Trading:QuoteAllocationPercentPerTrade") ?? 2.0m, 0.01m, 100m),
            Math.Max(0m, configuration.GetValue<decimal?>("Trading:MaxQuotePerTrade") ?? 50m),
            Math.Max(0m, configuration.GetValue<decimal?>("Trading:MinQuotePerTrade") ?? 10m),
            Math.Max(0m, configuration.GetValue<decimal?>("Trading:ReservedQuoteBalance") ?? 20m),
            configuration.GetValue<string>("Trading:BalanceAsset") ?? "USDT");
    }

    public static DecisionEngineRuntimeSettings ResolveDecisionEngine(IConfiguration configuration)
    {
        var symbolQuantities = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in configuration.GetSection("DecisionEngine:SymbolQuantities").GetChildren())
        {
            if (decimal.TryParse(child.Value, out var qty))
                symbolQuantities[child.Key] = qty;
        }

        var strategies = new Dictionary<string, DecisionStrategyRuntimeSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var strategy in configuration.GetSection("DecisionEngine:Strategies").GetChildren())
        {
            strategies[strategy.Key] = new DecisionStrategyRuntimeSettings(
                strategy.GetValue<decimal?>("MinConfidence"),
                strategy.GetValue<decimal?>("ExitMinConfidence"));
        }

        return new DecisionEngineRuntimeSettings(
            configuration.GetValue<decimal?>("DecisionEngine:MinConfidence"),
            configuration.GetValue<decimal?>("DecisionEngine:ExitMinConfidence"),
            configuration.GetValue<decimal?>("DecisionEngine:MinimumSignalConfidence") ?? 0.60m,
            configuration.GetValue<decimal?>("DecisionEngine:MinExecutionConfidence")
                ?? configuration.GetValue<decimal?>("DecisionEngine:MinConfidence")
                ?? configuration.GetValue<decimal?>("DecisionEngine:MinConfidenceThreshold")
                ?? DefaultMinConfidence,
            configuration.GetValue<decimal?>("DecisionEngine:Quantity") ?? 0.001m,
            Math.Max(1, configuration.GetValue<int?>("DecisionEngine:IntervalSeconds") ?? 30),
            Math.Max(0, configuration.GetValue<int?>("DecisionEngine:TradeCooldownSeconds") ?? 60),
            Math.Max(10, configuration.GetValue<int?>("DecisionEngine:IdempotencyWindowSeconds") ?? 120),
            Math.Max(1, configuration.GetValue<int?>("DecisionEngine:MaxMarketDataAgeSeconds") ?? 15),
            configuration.GetValue<bool?>("DecisionEngine:EnableSymbolRanking") ?? false,
            Math.Max(1, configuration.GetValue<int?>("DecisionEngine:MaxSymbolsToTradePerCycle") ?? 1),
            Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:MinOpportunityScore") ?? 0m),
            configuration.GetSection("DecisionEngine:Symbols").Get<string[]>() ?? [],
            configuration.GetValue<string>("DecisionEngine:Symbol") ?? "BTCUSDT",
            symbolQuantities,
            strategies);
    }

    public static MovingAverageStrategyRuntimeSettings ResolveMovingAverageStrategy(IConfiguration configuration)
    {
        const string prefix = "DecisionEngine:MovingAverageCrossoverStrategy";
        return new MovingAverageStrategyRuntimeSettings(
            Math.Max(2, configuration.GetValue<int?>($"{prefix}:ShortPeriod") ?? 10),
            Math.Max(3, configuration.GetValue<int?>($"{prefix}:LongPeriod") ?? 30),
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:MomentumFlatteningThreshold") ?? 0.00005m),
            Math.Max(0, configuration.GetValue<int?>($"{prefix}:CooldownSeconds") ?? 60),
            Math.Max(0, configuration.GetValue<int?>($"{prefix}:MinimumTrendConfidenceScore") ?? 70),
            Math.Max(0, configuration.GetValue<int?>($"{prefix}:MinimumMarketConditionScore") ?? 60),
            configuration.GetValue<bool?>($"{prefix}:AllowShortSelling") ?? false,
            configuration.GetValue<bool?>($"{prefix}:RequireCrossoverForEntry") ?? false,
            configuration.GetValue<bool?>($"{prefix}:ExitOnWeakTrendConfidence") ?? true,
            Math.Max(0, configuration.GetValue<int?>($"{prefix}:MinimumExitTrendConfidenceScore") ?? 40),
            configuration.GetValue<bool?>($"{prefix}:EnableLowVolatilityBreakoutEntry") ?? false,
            Math.Max(2, configuration.GetValue<int?>($"{prefix}:BreakoutLookbackCandles") ?? 10),
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:BreakoutBufferPercent") ?? 0.0005m),
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:MinBreakoutSlopePercent") ?? 0.0002m),
            configuration.GetValue<bool?>($"{prefix}:RequirePositiveShortSlopeForBreakout") ?? true,
            configuration.GetValue<bool?>($"{prefix}:RequireTrendStrengthExpansion") ?? true,
            configuration.GetValue<bool?>($"{prefix}:RequireBreakoutConfirmation") ?? true,
            Math.Max(1, configuration.GetValue<int?>($"{prefix}:BreakoutConfirmationCandles") ?? 1),
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:BreakoutHoldBufferPercent") ?? 0m),
            configuration.GetValue<bool?>($"{prefix}:RequireCloseAboveBreakoutThreshold") ?? true,
            configuration.GetValue<bool?>($"{prefix}:RequireShortSlopeStillPositiveOnConfirmation") ?? true,
            configuration.GetValue<bool?>($"{prefix}:RequireNoImmediateBearishCandleAfterBreakout") ?? false,
            configuration.GetValue<bool?>($"{prefix}:EnableNormalTrendFallbackWhenLowVolBreakoutFails") ?? false,
            configuration.GetValue<bool?>($"{prefix}:EnableNetAwareMomentumExit") ?? false,
            Math.Max(0, configuration.GetValue<int?>($"{prefix}:MomentumExitMinTradeAgeMinutes") ?? 5),
            Math.Min(0m, configuration.GetValue<decimal?>($"{prefix}:MomentumExitAllowIfUnrealizedLossPercentBelow") ?? -0.20m),
            configuration.GetValue<bool?>($"{prefix}:MomentumExitRequireBearishConfirmationWhenFeeNegative") ?? true,
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:MomentumExitMinNetProfitPercent") ?? 0.10m),
            configuration.GetValue<bool?>($"{prefix}:EnableNormalTrendBullishPersistenceFilter") ?? false,
            Math.Max(1, configuration.GetValue<int?>($"{prefix}:NormalTrendMinBullishPersistenceCandles") ?? 2),
            configuration.GetValue<bool?>($"{prefix}:EnableNormalTrendCloseAboveRecentHighFilter") ?? false,
            configuration.GetValue<bool?>($"{prefix}:EnableNormalTrendMinDistanceToInvalidationFilter") ?? false,
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:NormalTrendMinDistanceToInvalidationPercent") ?? 0.15m),
            configuration.GetValue<bool?>($"{prefix}:EnableNormalTrendRejectPreviousBearishCandleFilter") ?? false,
            configuration.GetValue<bool?>($"{prefix}:EnableNormalTrendRewardRiskFilter") ?? false,
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:NormalTrendMinExpectedRewardRisk") ?? 0.80m),
            configuration.GetValue<bool?>($"{prefix}:EnableNormalTrendNearRecentHighRejection") ?? false,
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:NormalTrendNearRecentHighRequiresRewardRisk") ?? 1.20m),
            configuration.GetValue<decimal?>($"{prefix}:NormalTrendNearRecentHighRequiresTrendStrengthPercent") is { } nearHighTrendStrength
                ? Math.Max(0m, nearHighTrendStrength)
                : null,
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:NormalTrendNearRecentHighPercent") ?? 0.15m),
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:NormalTrendAtrExtensionMultiplier") ?? 0.35m),
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:NormalTrendStructureExtensionMultiplier") ?? 0.35m),
            Math.Max(2, configuration.GetValue<int?>($"{prefix}:NormalTrendExpectedTargetLookbackCandles")
                ?? Math.Max(2, configuration.GetValue<int?>($"{prefix}:BreakoutLookbackCandles") ?? 10)),
            configuration.GetValue<bool?>($"{prefix}:NormalTrendUseMinAtrStructureExtension") ?? true,
            configuration.GetValue<bool?>($"{prefix}:UseConfirmedClosedCandlesForEntryQuality") ?? false,
            configuration.GetValue<bool?>($"{prefix}:UseConfirmedClosedCandlesForLowVolBreakout") ?? false,
            configuration.GetValue<bool?>($"{prefix}:EnableNormalTrendPullbackContinuationOverride") ?? false,
            Math.Max(0m, configuration.GetValue<decimal?>($"{prefix}:NormalTrendPullbackMinExpectedRewardRisk") ?? 0.80m),
            configuration.GetValue<bool?>($"{prefix}:NormalTrendPullbackRequireCloseAboveShortAndLongMa") ?? true,
            configuration.GetValue<bool?>($"{prefix}:NormalTrendPullbackRequirePositiveShortSlope") ?? true,
            configuration.GetValue<bool?>($"{prefix}:NormalTrendPullbackRejectPreviousBearishCandle") ?? true,
            configuration.GetValue<bool?>($"{prefix}:EnablePullbackOverrideHighVolatilityBlock") ?? false,
            configuration.GetValue<bool?>($"{prefix}:EnableNormalTrendPullbackReclaimConfirmationFilter") ?? false,
            configuration.GetValue<string>($"{prefix}:NormalTrendPullbackReclaimMode") ?? "PreviousCandleHigh");
    }

    public static TradeMonitoringRuntimeSettings ResolveTradeMonitoring(IConfiguration configuration)
    {
        return new TradeMonitoringRuntimeSettings(
            Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:IntervalSeconds") ?? 10),
            Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:MaxTradeDurationMinutes") ?? 60),
            Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:CloseOrderMaxRetries") ?? 3),
            Math.Max(100, configuration.GetValue<int?>("TradeMonitoring:CloseOrderRetryDelayMs") ?? 1000),
            configuration.GetValue<bool?>("TradeMonitoring:EnableDynamicTimeExit") ?? false,
            Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:FirstReviewMinutes") ?? 10),
            Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:CloseEarlyIfLossAfterMinutes") ?? 12),
            Math.Clamp(configuration.GetValue<decimal?>("TradeMonitoring:EarlyExitLossPercent") ?? 0.10m, 0m, 50m),
            Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:ExtensionMinutes") ?? 10),
            Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:MaxExtendedTradeDurationMinutes") ?? 60),
            Math.Clamp(configuration.GetValue<decimal?>("TradeMonitoring:MinUnrealizedProfitPercentToExtend") ?? 0.15m, 0m, 50m));
    }

    public static RiskRuntimeSettings ResolveRisk(IConfiguration configuration)
    {
        return new RiskRuntimeSettings(
            configuration.GetValue<decimal?>("RiskSettings:MaxPositionQuote") ?? 10_000m,
            configuration.GetValue<decimal?>("RiskSettings:MaxOrderQuote") ?? 5_000m,
            configuration.GetValue<decimal?>("RiskSettings:MaxExposurePercent") ?? 50m,
            configuration.GetValue<decimal?>("RiskSettings:MinOrderQuote") ?? 5m,
            configuration.GetValue<decimal?>("RiskSettings:ReducedPositionMultiplier") ?? 0.5m,
            configuration.GetValue<int?>("RiskSettings:MaxOpenPositions") ?? 3);
    }

    public static ExecutionRuntimeSettings ResolveExecution(IConfiguration configuration)
    {
        return new ExecutionRuntimeSettings(
            configuration.GetValue<bool?>("ExecutionSettings:Enabled") ?? false,
            configuration.GetValue<bool?>("ExecutionSettings:UseMarketOrders") ?? true);
    }

    public static ConfidenceResolution ResolveConfidenceThreshold(
        IConfiguration configuration,
        string strategyName,
        string action,
        string tradingMode,
        string executionIntent)
    {
        var normalized = NormalizeStrategyName(strategyName);
        var decision = ResolveDecisionEngine(configuration);

        decision.Strategies.TryGetValue(normalized, out var strategy);
        var entryThreshold = strategy?.MinConfidence ?? decision.MinConfidence ?? DefaultMinConfidence;
        var isSpotCloseLong = string.Equals(tradingMode, "Spot", StringComparison.OrdinalIgnoreCase)
                              && string.Equals(action, "Sell", StringComparison.OrdinalIgnoreCase)
                              && string.Equals(executionIntent, "CloseLong", StringComparison.OrdinalIgnoreCase);

        if (!isSpotCloseLong)
        {
            var source = strategy?.MinConfidence is not null
                ? $"Strategy:{normalized}:MinConfidence"
                : decision.MinConfidence is not null
                    ? "Global:DecisionEngine:MinConfidence"
                    : "Default:0.70";
            return new ConfidenceResolution(entryThreshold, "EntryMinConfidence", source);
        }

        var exitThreshold = strategy?.ExitMinConfidence ?? decision.ExitMinConfidence ?? entryThreshold;
        var exitSource = strategy?.ExitMinConfidence is not null
            ? $"Strategy:{normalized}:ExitMinConfidence"
            : decision.ExitMinConfidence is not null
                ? "Global:DecisionEngine:ExitMinConfidence"
                : strategy?.MinConfidence is not null
                    ? $"Fallback:Strategy:{normalized}:MinConfidence"
                    : decision.MinConfidence is not null
                        ? "Fallback:Global:DecisionEngine:MinConfidence"
                        : "Fallback:Default:0.70";
        return new ConfidenceResolution(exitThreshold, "ExitMinConfidence", exitSource);
    }

    public static IReadOnlyList<string> FindRuntimeTradingKeysInPlatform(
        IReadOnlyDictionary<string, string> appKeys,
        IReadOnlyDictionary<string, string> platformKeys)
    {
        var runtimePrefixes = new[]
        {
            "Trading:",
            "DecisionEngine:",
            "TradeMonitoring:",
            "RiskSettings:",
            "ExecutionSettings:",
            "OrderSync:",
            "TradeSync:",
            "PositionWorker:",
            "Workers:"
        };

        return platformKeys.Keys
            .Where(key => runtimePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Where(key => appKeys.ContainsKey(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static Dictionary<string, string> FlattenConfiguration(IConfiguration configuration)
    {
        return configuration.AsEnumerable()
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
            .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value!, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeStrategyName(string strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
            return "Unknown";

        var trimmed = strategyName.Trim();
        return trimmed.EndsWith("Strategy", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^"Strategy".Length]
            : trimmed;
    }
}

public sealed record TradingRuntimeSettings(
    string Mode,
    bool AllowAddToPosition,
    int MaxOpenPositionsPerSymbol,
    bool RequireStrategyExpectedTargetForSpotOpenLong,
    bool UseBalanceBasedSizing,
    decimal QuoteAllocationPercentPerTrade,
    decimal MaxQuotePerTrade,
    decimal MinQuotePerTrade,
    decimal ReservedQuoteBalance,
    string BalanceAsset);

public sealed record DecisionEngineRuntimeSettings(
    decimal? MinConfidence,
    decimal? ExitMinConfidence,
    decimal MinimumSignalConfidence,
    decimal MinExecutionConfidence,
    decimal Quantity,
    int IntervalSeconds,
    int TradeCooldownSeconds,
    int IdempotencyWindowSeconds,
    int MaxMarketDataAgeSeconds,
    bool EnableSymbolRanking,
    int MaxSymbolsToTradePerCycle,
    decimal MinOpportunityScore,
    IReadOnlyList<string> Symbols,
    string Symbol,
    IReadOnlyDictionary<string, decimal> SymbolQuantities,
    IReadOnlyDictionary<string, DecisionStrategyRuntimeSettings> Strategies);

public sealed record DecisionStrategyRuntimeSettings(
    decimal? MinConfidence,
    decimal? ExitMinConfidence);

public sealed record MovingAverageStrategyRuntimeSettings(
    int ShortPeriod,
    int LongPeriod,
    decimal MomentumFlatteningThreshold,
    int CooldownSeconds,
    int MinimumTrendConfidenceScore,
    int MinimumMarketConditionScore,
    bool AllowShortSelling,
    bool RequireCrossoverForEntry,
    bool ExitOnWeakTrendConfidence,
    int MinimumExitTrendConfidenceScore,
    bool EnableLowVolatilityBreakoutEntry,
    int BreakoutLookbackCandles,
    decimal BreakoutBufferPercent,
    decimal MinBreakoutSlopePercent,
    bool RequirePositiveShortSlopeForBreakout,
    bool RequireTrendStrengthExpansion,
    bool RequireBreakoutConfirmation,
    int BreakoutConfirmationCandles,
    decimal BreakoutHoldBufferPercent,
    bool RequireCloseAboveBreakoutThreshold,
    bool RequireShortSlopeStillPositiveOnConfirmation,
    bool RequireNoImmediateBearishCandleAfterBreakout,
    bool EnableNormalTrendFallbackWhenLowVolBreakoutFails,
    bool EnableNetAwareMomentumExit,
    int MomentumExitMinTradeAgeMinutes,
    decimal MomentumExitAllowIfUnrealizedLossPercentBelow,
    bool MomentumExitRequireBearishConfirmationWhenFeeNegative,
    decimal MomentumExitMinNetProfitPercent,
    bool EnableNormalTrendBullishPersistenceFilter,
    int NormalTrendMinBullishPersistenceCandles,
    bool EnableNormalTrendCloseAboveRecentHighFilter,
    bool EnableNormalTrendMinDistanceToInvalidationFilter,
    decimal NormalTrendMinDistanceToInvalidationPercent,
    bool EnableNormalTrendRejectPreviousBearishCandleFilter,
    bool EnableNormalTrendRewardRiskFilter,
    decimal NormalTrendMinExpectedRewardRisk,
    bool EnableNormalTrendNearRecentHighRejection,
    decimal NormalTrendNearRecentHighRequiresRewardRisk,
    decimal? NormalTrendNearRecentHighRequiresTrendStrengthPercent,
    decimal NormalTrendNearRecentHighPercent,
    decimal NormalTrendAtrExtensionMultiplier,
    decimal NormalTrendStructureExtensionMultiplier,
    int NormalTrendExpectedTargetLookbackCandles,
    bool NormalTrendUseMinAtrStructureExtension,
    bool UseConfirmedClosedCandlesForEntryQuality,
    bool UseConfirmedClosedCandlesForLowVolBreakout,
    bool EnableNormalTrendPullbackContinuationOverride,
    decimal NormalTrendPullbackMinExpectedRewardRisk,
    bool NormalTrendPullbackRequireCloseAboveShortAndLongMa,
    bool NormalTrendPullbackRequirePositiveShortSlope,
    bool NormalTrendPullbackRejectPreviousBearishCandle,
    bool EnablePullbackOverrideHighVolatilityBlock,
    bool EnableNormalTrendPullbackReclaimConfirmationFilter,
    string NormalTrendPullbackReclaimMode);

public sealed record TradeMonitoringRuntimeSettings(
    int IntervalSeconds,
    int MaxTradeDurationMinutes,
    int CloseOrderMaxRetries,
    int CloseOrderRetryDelayMs,
    bool EnableDynamicTimeExit,
    int FirstReviewMinutes,
    int CloseEarlyIfLossAfterMinutes,
    decimal EarlyExitLossPercent,
    int ExtensionMinutes,
    int MaxExtendedTradeDurationMinutes,
    decimal MinUnrealizedProfitPercentToExtend);

public sealed record RiskRuntimeSettings(
    decimal MaxPositionQuote,
    decimal MaxOrderQuote,
    decimal MaxExposurePercent,
    decimal MinOrderQuote,
    decimal ReducedPositionMultiplier,
    int MaxOpenPositions);

public sealed record ExecutionRuntimeSettings(
    bool Enabled,
    bool UseMarketOrders);

public sealed record ConfidenceResolution(
    decimal MinConfidence,
    string ThresholdKind,
    string ThresholdSource);
