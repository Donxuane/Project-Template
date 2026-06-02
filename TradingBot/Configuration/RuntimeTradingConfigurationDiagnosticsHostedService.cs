using Microsoft.Extensions.Hosting;
using TradingBot.Shared.Configuration;

namespace TradingBot.Configuration;

public sealed class RuntimeTradingConfigurationDiagnosticsHostedService(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<RuntimeTradingConfigurationDiagnosticsHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var contentRoot = hostEnvironment.ContentRootPath;
        var appConfig = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();
        var platformConfig = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("platformSettings.json", optional: true, reloadOnChange: false)
            .Build();

        var appKeys = RuntimeTradingConfigResolver.FlattenConfiguration(appConfig);
        var platformKeys = RuntimeTradingConfigResolver.FlattenConfiguration(platformConfig);
        var duplicates = RuntimeTradingConfigResolver.FindRuntimeTradingKeysInPlatform(appKeys, platformKeys);
        if (duplicates.Count > 0)
        {
            logger.LogWarning(
                "Runtime trading config key exists in platformSettings.json but appsettings.json is authoritative. Keys={Keys}",
                string.Join(", ", duplicates));

            if (hostEnvironment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"Runtime trading config keys must not be duplicated in platformSettings.json. Duplicate keys: {string.Join(", ", duplicates)}");
            }
        }

        LogResolvedRuntimeSettings();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void LogResolvedRuntimeSettings()
    {
        var trading = RuntimeTradingConfigResolver.ResolveTrading(configuration);
        var decision = RuntimeTradingConfigResolver.ResolveDecisionEngine(configuration);
        var movingAverage = RuntimeTradingConfigResolver.ResolveMovingAverageStrategy(configuration);
        var monitoring = RuntimeTradingConfigResolver.ResolveTradeMonitoring(configuration);
        var risk = RuntimeTradingConfigResolver.ResolveRisk(configuration);
        var execution = RuntimeTradingConfigResolver.ResolveExecution(configuration);

        logger.LogInformation(
            "Runtime trading settings resolved: TradingMode={TradingMode}, ExecutionEnabled={ExecutionEnabled}, MinConfidence={MinConfidence}, ExitMinConfidence={ExitMinConfidence}, EnableSymbolRanking={EnableSymbolRanking}, MaxSymbolsToTradePerCycle={MaxSymbolsToTradePerCycle}, MinOpportunityScore={MinOpportunityScore}, SymbolQuantities={SymbolQuantities}, StrategyMinConfidence={StrategyMinConfidence}, StrategyExitMinConfidence={StrategyExitMinConfidence}, MA.RequireCrossoverForEntry={RequireCrossoverForEntry}, MA.MinimumTrendConfidenceScore={MinimumTrendConfidenceScore}, MA.MinimumMarketConditionScore={MinimumMarketConditionScore}, MA.EnableLowVolatilityBreakoutEntry={EnableLowVolatilityBreakoutEntry}, MA.BreakoutLookbackCandles={BreakoutLookbackCandles}, MA.BreakoutBufferPercent={BreakoutBufferPercent}, MA.MinBreakoutSlopePercent={MinBreakoutSlopePercent}, MA.RequireBreakoutConfirmation={RequireBreakoutConfirmation}, MA.BreakoutConfirmationCandles={BreakoutConfirmationCandles}, MA.BreakoutHoldBufferPercent={BreakoutHoldBufferPercent}, MA.RequireCloseAboveBreakoutThreshold={RequireCloseAboveBreakoutThreshold}, MA.RequireShortSlopeStillPositiveOnConfirmation={RequireShortSlopeStillPositiveOnConfirmation}, MA.RequireNoImmediateBearishCandleAfterBreakout={RequireNoImmediateBearishCandleAfterBreakout}, MA.EnableNetAwareMomentumExit={EnableNetAwareMomentumExit}, MA.MomentumExitMinTradeAgeMinutes={MomentumExitMinTradeAgeMinutes}, MA.MomentumExitAllowIfUnrealizedLossPercentBelow={MomentumExitAllowIfUnrealizedLossPercentBelow}, MA.MomentumExitRequireBearishConfirmationWhenFeeNegative={MomentumExitRequireBearishConfirmationWhenFeeNegative}, MA.MomentumExitMinNetProfitPercent={MomentumExitMinNetProfitPercent}, TradeMonitoring.DynamicTimeExit={DynamicTimeExit}, TradeMonitoring.FirstReviewMinutes={FirstReviewMinutes}, TradeMonitoring.CloseEarlyIfLossAfterMinutes={CloseEarlyIfLossAfterMinutes}, TradeMonitoring.EarlyExitLossPercent={EarlyExitLossPercent}, Risk.MaxOpenPositions={MaxOpenPositions}, Trading.MaxOpenPositionsPerSymbol={MaxOpenPositionsPerSymbol}",
            trading.Mode,
            execution.Enabled,
            decision.MinConfidence,
            decision.ExitMinConfidence,
            decision.EnableSymbolRanking,
            decision.MaxSymbolsToTradePerCycle,
            decision.MinOpportunityScore,
            string.Join(", ", decision.SymbolQuantities.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")),
            decision.Strategies.TryGetValue("MovingAverageCrossover", out var strategy) ? strategy.MinConfidence : null,
            decision.Strategies.TryGetValue("MovingAverageCrossover", out strategy) ? strategy.ExitMinConfidence : null,
            movingAverage.RequireCrossoverForEntry,
            movingAverage.MinimumTrendConfidenceScore,
            movingAverage.MinimumMarketConditionScore,
            movingAverage.EnableLowVolatilityBreakoutEntry,
            movingAverage.BreakoutLookbackCandles,
            movingAverage.BreakoutBufferPercent,
            movingAverage.MinBreakoutSlopePercent,
            movingAverage.RequireBreakoutConfirmation,
            movingAverage.BreakoutConfirmationCandles,
            movingAverage.BreakoutHoldBufferPercent,
            movingAverage.RequireCloseAboveBreakoutThreshold,
            movingAverage.RequireShortSlopeStillPositiveOnConfirmation,
            movingAverage.RequireNoImmediateBearishCandleAfterBreakout,
            movingAverage.EnableNetAwareMomentumExit,
            movingAverage.MomentumExitMinTradeAgeMinutes,
            movingAverage.MomentumExitAllowIfUnrealizedLossPercentBelow,
            movingAverage.MomentumExitRequireBearishConfirmationWhenFeeNegative,
            movingAverage.MomentumExitMinNetProfitPercent,
            monitoring.EnableDynamicTimeExit,
            monitoring.FirstReviewMinutes,
            monitoring.CloseEarlyIfLossAfterMinutes,
            monitoring.EarlyExitLossPercent,
            risk.MaxOpenPositions,
            trading.MaxOpenPositionsPerSymbol);

        logger.LogInformation(
            "Runtime trading normal-trend target projection settings resolved: MA.NormalTrendAtrExtensionMultiplier={NormalTrendAtrExtensionMultiplier}, MA.NormalTrendStructureExtensionMultiplier={NormalTrendStructureExtensionMultiplier}, MA.NormalTrendExpectedTargetLookbackCandles={NormalTrendExpectedTargetLookbackCandles}, MA.NormalTrendUseMinAtrStructureExtension={NormalTrendUseMinAtrStructureExtension}",
            movingAverage.NormalTrendAtrExtensionMultiplier,
            movingAverage.NormalTrendStructureExtensionMultiplier,
            movingAverage.NormalTrendExpectedTargetLookbackCandles,
            movingAverage.NormalTrendUseMinAtrStructureExtension);
    }
}
