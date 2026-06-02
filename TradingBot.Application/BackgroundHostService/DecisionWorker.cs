using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;
using TradingBot.Domain.Models.Trading;
using TradingBot.Shared.Configuration;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Runs the decision pipeline on a fixed interval.
/// V1 only generates/logs decisions (execution is intentionally separate).
/// </summary>
public class DecisionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DecisionWorker> logger) : BackgroundService
{
    private const int DefaultIntervalSeconds = 30;
    private const decimal DefaultQuantity = 0.001m;
    private const string DefaultSymbol = nameof(TradingSymbol.BTCUSDT);
    private const decimal DefaultMinExecutionConfidence = 0.70m;
    private const int DefaultTradeCooldownSeconds = 60;
    private const int DefaultIdempotencyWindowSeconds = 120;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = ReadSettings();
        if (settings.Quantity <= 0m)
        {
            logger.LogError(
                "DecisionWorker invalid quantity configuration. DecisionEngine:Quantity must be greater than zero. Current value={Quantity}",
                settings.Quantity);
            return;
        }

        logger.LogInformation(
            "DecisionWorker started. Symbols={Symbols}, Quantity={Quantity}, Interval={IntervalSeconds}s, MinExecutionConfidence={MinExecutionConfidence}, TradeCooldownSeconds={TradeCooldownSeconds}, IdempotencyWindowSeconds={IdempotencyWindowSeconds}, ExecutionEnabled={ExecutionEnabled}, UseMarketOrders={UseMarketOrders}, EnableSymbolRanking={EnableSymbolRanking}, MaxSymbolsToTradePerCycle={MaxSymbolsToTradePerCycle}, MinOpportunityScore={MinOpportunityScore}, GlobalMaxOpenPositions={GlobalMaxOpenPositions}",
            string.Join(",", settings.Symbols), settings.Quantity, settings.IntervalSeconds, settings.MinExecutionConfidence, settings.TradeCooldownSeconds, settings.IdempotencyWindowSeconds, settings.ExecutionEnabled, settings.UseMarketOrders, settings.EnableSymbolRanking, settings.MaxSymbolsToTradePerCycle, settings.MinOpportunityScore, settings.GlobalMaxOpenPositions);

        using (var warmupScope = scopeFactory.CreateScope())
        {
            var candleWarmupService = warmupScope.ServiceProvider.GetRequiredService<ICandleWarmupService>();
            await candleWarmupService.WarmUpAsync(settings.Symbols, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var correlationId = Guid.NewGuid().ToString("N");
                var cycleStartUtc = DateTime.UtcNow;
                var cycleTimer = Stopwatch.StartNew();
                var selectedSymbols = new List<TradingSymbol>();
                var currentOpenPositions = 0;
                var availableOpenSlots = 0;
                using (var scope = scopeFactory.CreateScope())
                {
                    var tradeDecisionService = scope.ServiceProvider.GetRequiredService<TradeDecisionService>();
                    var riskManagementService = scope.ServiceProvider.GetRequiredService<IRiskManagementService>();
                    var tradeExecutionService = scope.ServiceProvider.GetRequiredService<ITradeExecutionService>();
                    var tradeCooldownService = scope.ServiceProvider.GetRequiredService<ITradeCooldownService>();
                    var tradeIdempotencyService = scope.ServiceProvider.GetRequiredService<ITradeIdempotencyService>();
                    var tradeExecutionDesicionsRepository = scope.ServiceProvider.GetRequiredService<ITradeExecutionDesicionsRepository>();
                    var positionExecutionGuard = scope.ServiceProvider.GetRequiredService<IPositionExecutionGuard>();
                    var confidenceGate = scope.ServiceProvider.GetRequiredService<IConfidenceGate>();
                    var positionRepository = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
                    var spotPositionSizingService = scope.ServiceProvider.GetRequiredService<ISpotPositionSizingService>();
                    if (settings.EnableSymbolRanking && settings.Symbols.Count > 1)
                    {
                        var rankingSummary = await ProcessRankedSymbolsAsync(
                            settings,
                            correlationId,
                            stoppingToken,
                            tradeDecisionService,
                            riskManagementService,
                            tradeExecutionService,
                            tradeCooldownService,
                            tradeIdempotencyService,
                            tradeExecutionDesicionsRepository,
                            positionExecutionGuard,
                            confidenceGate,
                            positionRepository,
                            spotPositionSizingService);
                        selectedSymbols = rankingSummary.SelectedSymbols.ToList();
                        currentOpenPositions = rankingSummary.CurrentOpenPositions;
                        availableOpenSlots = rankingSummary.AvailableOpenSlots;
                    }
                    else
                    {
                        var openPositions = await positionRepository.GetOpenPositionsAsync(stoppingToken);
                        currentOpenPositions = openPositions.Count(p => p.IsOpen);
                        availableOpenSlots = Math.Max(0, settings.GlobalMaxOpenPositions - currentOpenPositions);
                        selectedSymbols = settings.Symbols.ToList();

                        foreach (var symbol in settings.Symbols)
                        {
                            var symbolEvalStartUtc = DateTime.UtcNow;
                            var symbolTimer = Stopwatch.StartNew();
                            var quantityTimer = Stopwatch.StartNew();
                            var sizingResult = await ResolveOpenLongQuantityAsync(
                                symbol,
                                settings,
                                spotPositionSizingService,
                                stoppingToken);
                            quantityTimer.Stop();
                            if (!sizingResult.IsSuccess)
                            {
                                logger.LogWarning(
                                    "DecisionWorker skipped symbol due to position sizing rejection: Symbol={Symbol}, Reason={Reason}, QuantitySource={QuantitySource}",
                                    symbol,
                                    sizingResult.Reason,
                                    sizingResult.QuantitySource);
                                symbolTimer.Stop();
                                logger.LogInformation(
                                    "DecisionWorker symbol evaluation timing: CorrelationId={CorrelationId}, Symbol={Symbol}, SymbolEvalStartUtc={SymbolEvalStartUtc}, SymbolEvalEndUtc={SymbolEvalEndUtc}, SymbolElapsedMs={SymbolElapsedMs}, QuantityResolveMs={QuantityResolveMs}, DecisionServiceMs={DecisionServiceMs}, StrategyMs={StrategyMs}, DecisionPersistMs={DecisionPersistMs}, FinalAction={FinalAction}, FinalReason={FinalReason}",
                                    correlationId,
                                    symbol,
                                    symbolEvalStartUtc,
                                    DateTime.UtcNow,
                                    symbolTimer.ElapsedMilliseconds,
                                    quantityTimer.ElapsedMilliseconds,
                                    0L,
                                    null,
                                    null,
                                    TradeSignal.Hold,
                                    sizingResult.Reason ?? "Position sizing rejected.");
                                continue;
                            }

                            var requestedQuantity = sizingResult.Quantity;
                            var hasSymbolOverride = sizingResult.QuantitySource == SpotQuantitySource.SymbolOverride;
                            logger.LogInformation(
                                "DecisionWorker resolved symbol quantity: Symbol={Symbol}, RequestedQuantity={RequestedQuantity}, HasSymbolOverride={HasSymbolOverride}, GlobalQuantity={GlobalQuantity}, QuantitySource={QuantitySource}, AvailableQuoteBalance={AvailableQuoteBalance}, ReservedQuoteBalance={ReservedQuoteBalance}, UsableQuoteBalance={UsableQuoteBalance}, QuoteAllocationPercentPerTrade={QuoteAllocationPercentPerTrade}, DesiredQuoteAmount={DesiredQuoteAmount}, CappedQuoteAmount={CappedQuoteAmount}, CurrentPrice={CurrentPrice}, RawQuantity={RawQuantity}, NormalizedQuantity={NormalizedQuantity}, FinalNotional={FinalNotional}, MinNotional={MinNotional}",
                                symbol,
                                requestedQuantity,
                                hasSymbolOverride,
                                settings.Quantity,
                                sizingResult.QuantitySource,
                                sizingResult.AvailableQuoteBalance,
                                sizingResult.ReservedQuoteBalance,
                                sizingResult.UsableQuoteBalance,
                                sizingResult.QuoteAllocationPercentPerTrade,
                                sizingResult.DesiredQuoteAmount,
                                sizingResult.CappedQuoteAmount,
                                sizingResult.CurrentPrice,
                                sizingResult.RawQuantity,
                                sizingResult.NormalizedQuantity,
                                sizingResult.FinalNotional,
                                sizingResult.MinNotional);
                            var symbolMetrics = await ProcessSymbolAsync(
                                symbol,
                                requestedQuantity,
                                settings,
                                correlationId,
                                stoppingToken,
                                tradeDecisionService,
                                riskManagementService,
                                tradeExecutionService,
                                tradeCooldownService,
                                tradeIdempotencyService,
                                tradeExecutionDesicionsRepository,
                                positionExecutionGuard,
                                confidenceGate,
                                spotPositionSizingService);
                            symbolTimer.Stop();
                            logger.LogInformation(
                                "DecisionWorker symbol evaluation timing: CorrelationId={CorrelationId}, Symbol={Symbol}, SymbolEvalStartUtc={SymbolEvalStartUtc}, SymbolEvalEndUtc={SymbolEvalEndUtc}, SymbolElapsedMs={SymbolElapsedMs}, QuantityResolveMs={QuantityResolveMs}, DecisionServiceMs={DecisionServiceMs}, StrategyMs={StrategyMs}, DecisionPersistMs={DecisionPersistMs}, FinalAction={FinalAction}, FinalReason={FinalReason}",
                                correlationId,
                                symbol,
                                symbolEvalStartUtc,
                                DateTime.UtcNow,
                                symbolTimer.ElapsedMilliseconds,
                                quantityTimer.ElapsedMilliseconds,
                                symbolMetrics.DecisionServiceMs,
                                symbolMetrics.StrategyMs,
                                symbolMetrics.DecisionPersistMs,
                                symbolMetrics.FinalAction,
                                symbolMetrics.FinalReason);
                        }
                    }
                }
                cycleTimer.Stop();
                var cycleEndUtc = DateTime.UtcNow;
                logger.LogInformation(
                    "DecisionWorker cycle timing: CorrelationId={CorrelationId}, CycleStartUtc={CycleStartUtc}, CycleEndUtc={CycleEndUtc}, CycleElapsedMs={CycleElapsedMs}, ConfiguredIntervalSeconds={ConfiguredIntervalSeconds}, SymbolsCount={SymbolsCount}, RankingEnabled={RankingEnabled}, SelectedSymbols={SelectedSymbols}, CurrentOpenPositions={CurrentOpenPositions}, AvailableOpenSlots={AvailableOpenSlots}",
                    correlationId,
                    cycleStartUtc,
                    cycleEndUtc,
                    cycleTimer.ElapsedMilliseconds,
                    settings.IntervalSeconds,
                    settings.Symbols.Count,
                    settings.EnableSymbolRanking,
                    string.Join(",", selectedSymbols),
                    currentOpenPositions,
                    availableOpenSlots);

            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DecisionWorker cycle failed at {Time}", DateTime.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), stoppingToken);
        }

        logger.LogInformation("DecisionWorker stopped.");
    }

    private async Task<SymbolProcessingMetrics> ProcessSymbolAsync(
        TradingSymbol symbol,
        decimal requestedQuantity,
        DecisionWorkerSettings settings,
        string correlationId,
        CancellationToken stoppingToken,
        TradeDecisionService tradeDecisionService,
        IRiskManagementService riskManagementService,
        ITradeExecutionService tradeExecutionService,
        ITradeCooldownService tradeCooldownService,
        ITradeIdempotencyService tradeIdempotencyService,
        ITradeExecutionDesicionsRepository tradeExecutionDesicionsRepository,
        IPositionExecutionGuard positionExecutionGuard,
        IConfidenceGate confidenceGate,
        ISpotPositionSizingService spotPositionSizingService,
        DecisionResult? precomputedDecision = null
        )
    {
        var decisionServiceMs = 0L;
        decimal? strategyMs = null;
        long? decisionPersistMs = null;
        try
        {
            DecisionResult decision;
            if (precomputedDecision is null)
            {
                var decisionTimer = Stopwatch.StartNew();
                decision = await tradeDecisionService.MakeDecision(symbol, requestedQuantity, stoppingToken);
                decisionTimer.Stop();
                decisionServiceMs = decisionTimer.ElapsedMilliseconds;
            }
            else
            {
                decision = precomputedDecision;
            }

            strategyMs = null;
            var side = decision.Candidate?.Side ?? InferSideFromAction(decision.Action);
            var rawSignal = decision.RawSignal == default && decision.Action != TradeSignal.Hold
                ? decision.Action
                : decision.RawSignal;
            var tradingMode = decision.Candidate?.TradingMode ?? decision.TradingMode;
            var executionIntent = decision.Candidate?.ExecutionIntent ?? decision.ExecutionIntent;
            var isSpotOpenLong = decision.Action == TradeSignal.Buy
                                 && tradingMode == TradingMode.Spot
                                 && executionIntent == TradeExecutionIntent.OpenLong;
            var strategyName = string.IsNullOrWhiteSpace(decision.StrategyName) ? "Unknown" : decision.StrategyName;
            var executionQuantity = decision.Candidate?.Quantity ?? requestedQuantity;
            var price = decision.Candidate?.Price ?? 0m;
            var decisionId = CreateDecisionId(
                correlationId,
                symbol,
                decision.Action,
                side,
                executionQuantity,
                price);
            var idempotencyKey = CreateIdempotencyKey(
                symbol,
                decision.Action,
                side,
                executionQuantity,
                settings.IdempotencyWindowSeconds);

            logger.LogInformation(
                "DecisionWorker decision generated: CorrelationId={CorrelationId}, DecisionId={DecisionId}, IdempotencyKey={IdempotencyKey}, Symbol={Symbol}, Action={Action}, Side={Side}, Quantity={Quantity}, Price={Price}, Confidence={Confidence:F4}, Reason={Reason}",
                correlationId, decisionId, idempotencyKey, symbol, decision.Action, side, executionQuantity, price, decision.Confidence, decision.Reason);
            logger.LogInformation(
                "DecisionWorker execution intent mapped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, StrategyName={StrategyName}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Side={Side}, Quantity={Quantity}",
                correlationId, decisionId, strategyName, symbol, rawSignal, tradingMode, executionIntent, side, executionQuantity);
            var executionDesicion = new TradeExecutionDecisions
            {
                CorrelationId = correlationId,
                DecisionId = decisionId,
                IdempotencyKey = idempotencyKey,
                StrategyName = strategyName,
                Symbol = symbol,
                Action = decision.Action,
                RawSignal = rawSignal,
                TradingMode = tradingMode,
                ExecutionIntent = executionIntent,
                Side = side,
                DecisionStatus = DecisionStatus.Pending,
                GuardStage = GuardStage.None,
                Confidence = decision.Confidence,
                Reason = decision.Reason,
                MinConfidence = null,
                ExpectedMovePercent = isSpotOpenLong ? decision.Candidate?.ExpectedMovePercent : null,
                ExpectedTargetPrice = isSpotOpenLong ? decision.Candidate?.ExpectedTargetPrice : null,
                ExpectedTargetSource = isSpotOpenLong ? decision.Candidate?.ExpectedTargetSource : null,
                TrendConfidenceScore = isSpotOpenLong ? decision.Candidate?.TrendConfidenceScore ?? decision.TrendConfidenceScore : null,
                MarketConditionScore = isSpotOpenLong ? decision.Candidate?.MarketConditionScore ?? decision.MarketConditionScore : null,
                VolatilityRegime = isSpotOpenLong ? decision.Candidate?.VolatilityRegime : null,
                RequiresReducedPositionSize = isSpotOpenLong ? decision.Candidate?.RequiresReducedPositionSize : null,
                ConsecutiveBullishTrendCandles = isSpotOpenLong ? decision.Candidate?.ConsecutiveBullishTrendCandles : null,
                CurrentCloseAboveRecentHigh = isSpotOpenLong ? decision.Candidate?.CurrentCloseAboveRecentHigh : null,
                DistanceToInvalidationPercent = isSpotOpenLong ? decision.Candidate?.DistanceToInvalidationPercent : null,
                PreviousCandleBearish = isSpotOpenLong ? decision.Candidate?.PreviousCandleBearish : null,
                EntryNearRecentHigh = isSpotOpenLong ? decision.Candidate?.EntryNearRecentHigh : null,
                ShortMaSlopePercent = isSpotOpenLong ? decision.Candidate?.ShortMaSlopePercent : null,
                TrendStrengthPercent = isSpotOpenLong ? decision.Candidate?.TrendStrengthPercent : null,
                ProjectionMode = isSpotOpenLong ? decision.Candidate?.ProjectionMode : null,
                ProjectedExtension = isSpotOpenLong ? decision.Candidate?.ProjectedExtension : null
            };


            var persistTimer = Stopwatch.StartNew();
            _ = await tradeExecutionDesicionsRepository.AddDesicionAsync(executionDesicion);
            persistTimer.Stop();
            decisionPersistMs = persistTimer.ElapsedMilliseconds;

            SymbolProcessingMetrics TerminalMetrics(string finalStage, string finalReason)
            {
                logger.LogInformation(
                    "DecisionWorker terminal decision: CorrelationId={CorrelationId}, Symbol={Symbol}, FinalStage={FinalStage}, FinalReason={FinalReason}, Action={Action}, ExecutionIntent={ExecutionIntent}, DecisionOutcome={DecisionOutcome}, GuardStage={GuardStage}",
                    correlationId,
                    symbol,
                    finalStage,
                    finalReason,
                    decision.Action,
                    executionIntent,
                    executionDesicion.DecisionStatus,
                    executionDesicion.GuardStage);

                return new SymbolProcessingMetrics(
                    decisionServiceMs,
                    strategyMs,
                    decisionPersistMs,
                    decision.Action,
                    finalReason);
            }
            
            if (decision.Action == TradeSignal.Hold)
            {
                executionDesicion.ExecutionError = "Execution skipped - hold action.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("Hold", executionDesicion.ExecutionError);
            }

            if (decision.Action != TradeSignal.Buy && decision.Action != TradeSignal.Sell)
            {
                executionDesicion.ExecutionError = "Execution skipped - unsupported action.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("UnsupportedAction", executionDesicion.ExecutionError);
            }

            if (!settings.ExecutionEnabled)
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Side={Side}, Quantity={Quantity}, SkipReason={SkipReason}",
                    correlationId, decisionId, symbol, rawSignal, tradingMode, executionIntent, side, executionQuantity, "SafeModeDisabled");
                executionDesicion.ExecutionError = "Execution skipped - execution is disabled.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("ExecutionDisabled", executionDesicion.ExecutionError);
            }

            if (!settings.UseMarketOrders)
            {
                logger.LogWarning(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Side={Side}, Quantity={Quantity}, SkipReason={SkipReason}",
                    correlationId, decisionId, symbol, rawSignal, tradingMode, executionIntent, side, executionQuantity, "OnlyMarketOrdersSupported");
                executionDesicion.ExecutionError = "Execution skipped - only market orders are supported.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("UnsupportedOrderType", executionDesicion.ExecutionError);
            }

            if (decision.Candidate is null || decision.Candidate.Price.GetValueOrDefault() <= 0m)
            {
                executionDesicion.ExecutionError = "Execution skipped - invalid trade candidate.";
                executionDesicion.RiskIsAllowed = false;
                executionDesicion.RiskReason = "Execution skipped - invalid trade candidate.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("InvalidCandidate", executionDesicion.ExecutionError);
            }

            var executionSide = decision.Candidate.Side;

            var guardResult = await positionExecutionGuard.EvaluateAsync(
                new PositionExecutionGuardRequest
                {
                    Symbol = symbol,
                    TradingMode = tradingMode,
                    RawSignal = rawSignal,
                    ExecutionIntent = executionIntent,
                    RequestedSide = executionSide,
                    RequestedQuantity = executionQuantity,
                    IsProtectiveExit = false
                },
                stoppingToken);
            if (!guardResult.IsAllowed)
            {
                executionDesicion.ExecutionError = guardResult.Reason;
                executionDesicion.RiskIsAllowed = false;
                executionDesicion.RiskReason = guardResult.Reason;
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                executionDesicion.GuardStage = tradingMode == TradingMode.Futures
                    ? GuardStage.UnsupportedMode
                    : GuardStage.PositionGuard;
                logger.LogInformation(
                    "DecisionWorker execution skipped by position guard: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, TradingMode={TradingMode}, RawSignal={RawSignal}, ExecutionIntent={ExecutionIntent}, RequestedSide={RequestedSide}, RequestedQuantity={RequestedQuantity}, OpenPositionQuantity={OpenPositionQuantity}, Allowed={Allowed}, Reason={Reason}",
                    correlationId, decisionId, symbol, tradingMode, rawSignal, executionIntent, executionSide, executionQuantity, guardResult.OpenPositionQuantity, false, guardResult.Reason);
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("PositionGuard", executionDesicion.ExecutionError ?? guardResult.Reason);
            }
            logger.LogInformation(
                "DecisionWorker position guard allowed execution: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, TradingMode={TradingMode}, RawSignal={RawSignal}, ExecutionIntent={ExecutionIntent}, RequestedSide={RequestedSide}, RequestedQuantity={RequestedQuantity}, OpenPositionQuantity={OpenPositionQuantity}, Allowed={Allowed}",
                correlationId, decisionId, symbol, tradingMode, rawSignal, executionIntent, executionSide, executionQuantity, guardResult.OpenPositionQuantity, true);

            var cooldown = await tradeCooldownService.CheckCooldownAsync(symbol, settings.TradeCooldownSeconds, stoppingToken);
            executionDesicion.IsInCooldown = cooldown.IsInCooldown;
            executionDesicion.CooldownRemainingSeconds = cooldown.RemainingSeconds;
            executionDesicion.CooldownLastTrade = cooldown.LastTradeAtUtc;
            logger.LogInformation(
                "DecisionWorker gate snapshot: CorrelationId={CorrelationId}, DecisionId={DecisionId}, IdempotencyKey={IdempotencyKey}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Action={Action}, Side={Side}, IsInCooldown={IsInCooldown}, CooldownRemainingSeconds={CooldownRemainingSeconds}, IdempotencyDuplicate={IdempotencyDuplicate}, ExecutionState={ExecutionState}",
                correlationId,
                decisionId,
                idempotencyKey,
                symbol,
                rawSignal,
                tradingMode,
                executionIntent,
                decision.Action,
                executionSide,
                cooldown.IsInCooldown,
                cooldown.RemainingSeconds,
                false,
                "Pending");
            if (cooldown.IsInCooldown)
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, IdempotencyKey={IdempotencyKey}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Action={Action}, Side={Side}, IsInCooldown={IsInCooldown}, CooldownRemainingSeconds={CooldownRemainingSeconds}, IdempotencyDuplicate={IdempotencyDuplicate}, SkipReason={SkipReason}, LastTradeAtUtc={LastTradeAtUtc}, ExecutionState={ExecutionState}",
                    correlationId, decisionId, idempotencyKey, symbol, rawSignal, tradingMode, executionIntent, decision.Action, executionSide, cooldown.IsInCooldown, cooldown.RemainingSeconds, false, "CooldownActive", cooldown.LastTradeAtUtc, "Skipped");
                executionDesicion.ExecutionError = "Execution skipped - cooldown active.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                executionDesicion.GuardStage = GuardStage.Cooldown;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("Cooldown", executionDesicion.ExecutionError);
            }

            if (await tradeIdempotencyService.IsDuplicateDecisionAsync(idempotencyKey, settings.IdempotencyWindowSeconds, stoppingToken))
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, IdempotencyKey={IdempotencyKey}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Action={Action}, Side={Side}, IsInCooldown={IsInCooldown}, CooldownRemainingSeconds={CooldownRemainingSeconds}, IdempotencyDuplicate={IdempotencyDuplicate}, SkipReason={SkipReason}, ExecutionState={ExecutionState}",
                    correlationId, decisionId, idempotencyKey, symbol, rawSignal, tradingMode, executionIntent, decision.Action, executionSide, cooldown.IsInCooldown, cooldown.RemainingSeconds, true, "IdempotencyDuplicate", "Skipped");
                executionDesicion.IdempotencyDuplicate = true;
                executionDesicion.ExecutionError = "Execution skipped - idempotency duplicate.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                executionDesicion.GuardStage = GuardStage.Idempotency;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("Idempotency", executionDesicion.ExecutionError);
            }

            var requiresReducedPositionSize = decision.Candidate?.RequiresReducedPositionSize ?? false;
            if (decision.Action == TradeSignal.Buy
                && tradingMode == TradingMode.Spot
                && executionIntent == TradeExecutionIntent.OpenLong
                && requiresReducedPositionSize)
            {
                var minNotionalValidation = await spotPositionSizingService.ValidateMinNotionalAsync(
                    symbol,
                    executionQuantity,
                    price,
                    stoppingToken);
                if (!minNotionalValidation.IsValid)
                {
                    logger.LogWarning(
                        "DecisionWorker execution skipped after high-volatility reduction: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Quantity={Quantity}, Price={Price}, Notional={Notional}, MinNotional={MinNotional}, Reason={Reason}",
                        correlationId,
                        decisionId,
                        symbol,
                        executionQuantity,
                        price,
                        minNotionalValidation.Notional,
                        minNotionalValidation.MinNotional,
                        minNotionalValidation.Reason);
                    executionDesicion.ExecutionError = minNotionalValidation.Reason;
                    executionDesicion.RiskIsAllowed = false;
                    executionDesicion.RiskReason = minNotionalValidation.Reason;
                    executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                    executionDesicion.GuardStage = GuardStage.Risk;
                    await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                    return TerminalMetrics("Risk", executionDesicion.ExecutionError ?? minNotionalValidation.Reason ?? "Risk validation failed.");
                }
            }

            var riskResult = await riskManagementService.ValidateTrade(
                symbol,
                executionQuantity,
                price,
                executionSide,
                stoppingToken,
                requiresReducedPositionSize,
                tradingMode,
                rawSignal,
                executionIntent);

            executionDesicion.Side = executionSide;
            var shouldPersistProtectionTargets = !(tradingMode == TradingMode.Spot && executionIntent == TradeExecutionIntent.CloseLong);
            executionDesicion.StopLossPrice = shouldPersistProtectionTargets ? riskResult.StopLossPrice : null;
            executionDesicion.TakeProfitPrice = shouldPersistProtectionTargets ? riskResult.TakeProfitPrice : null;
            executionDesicion.RiskIsAllowed = riskResult.IsAllowed;
            executionDesicion.RiskReason = riskResult.Reason;
            // TODO: Persist additional risk payload when TradeExecutionDecisions model is extended:
            // RiskScore, ExposurePercent, AccountEquityQuote, RequiresReducedPositionSize.
            if (!riskResult.IsAllowed)
            {
                logger.LogWarning(
                    "DecisionWorker execution rejected by risk: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, Reason={Reason}",
                    correlationId, decisionId, symbol, executionSide, riskResult.Reason);
                executionDesicion.ExecutionError = "Execution skipped - final risk validation rejected the trade.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                executionDesicion.GuardStage = GuardStage.Risk;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("Risk", executionDesicion.ExecutionError);
            }

            logger.LogInformation(
                "DecisionWorker risk approved: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, StopLossPrice={StopLossPrice}, TakeProfitPrice={TakeProfitPrice}",
                correlationId, decisionId, symbol, executionSide, riskResult.StopLossPrice, riskResult.TakeProfitPrice);

            var confidenceResult = await confidenceGate.EvaluateAsync(
                new ConfidenceGateRequest
                {
                    StrategyName = strategyName,
                    Symbol = symbol,
                    Action = decision.Action,
                    TradingMode = tradingMode,
                    ExecutionIntent = executionIntent,
                    Confidence = decision.Confidence
                },
                stoppingToken);
            executionDesicion.MinConfidence = confidenceResult.MinConfidence;
            executionDesicion.Confidence = confidenceResult.Confidence;
            if (!confidenceResult.IsAllowed)
            {
                executionDesicion.ExecutionSuccess = false;
                executionDesicion.ExecutionError = "Confidence below minimum threshold.";
                executionDesicion.RiskIsAllowed = false;
                executionDesicion.RiskReason = "Confidence below minimum threshold.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                executionDesicion.GuardStage = GuardStage.ConfidenceGate;
                logger.LogInformation(
                    "DecisionWorker execution skipped by confidence gate: StrategyName={StrategyName}, CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Confidence={Confidence:F4}, MinConfidence={MinConfidence:F4}, Allowed={Allowed}, Reason={Reason}",
                    confidenceResult.StrategyName,
                    correlationId,
                    decisionId,
                    symbol,
                    rawSignal,
                    tradingMode,
                    executionIntent,
                    confidenceResult.Confidence,
                    confidenceResult.MinConfidence,
                    false,
                    confidenceResult.Reason);
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("ConfidenceGate", executionDesicion.ExecutionError ?? confidenceResult.Reason ?? "Confidence gate blocked execution.");
            }

            var executionResult = await tradeExecutionService.ExecuteMarketOrderAsync(
                new TradeExecutionRequest
                {
                    CorrelationId = correlationId,
                    DecisionId = decisionId,
                    Symbol = symbol,
                    Side = executionSide,
                    Quantity = executionQuantity,
                    TradingMode = tradingMode,
                    RawSignal = rawSignal,
                    ExecutionIntent = executionIntent,
                    RequiresReducedPositionSize = requiresReducedPositionSize,
                    CandidatePrice = price > 0m ? price : null,
                    ExpectedTargetPrice = decision.Candidate?.ExpectedTargetPrice,
                    ExpectedMovePercent = decision.Candidate?.ExpectedMovePercent,
                    ExpectedTargetSource = decision.Candidate?.ExpectedTargetSource,
                    BreakoutRangeHigh = decision.Candidate?.BreakoutRangeHigh,
                    BreakoutRangeLow = decision.Candidate?.BreakoutRangeLow,
                    BreakoutThresholdPrice = decision.Candidate?.BreakoutThresholdPrice,
                    ExpectedTargetStructureExtensionUsed = decision.Candidate?.ExpectedTargetStructureExtensionUsed,
                    ExpectedTargetAtrUsed = decision.Candidate?.ExpectedTargetAtrUsed
                },
                stoppingToken);

            if (executionResult.Success)
            {
                await tradeIdempotencyService.MarkDecisionExecutedAsync(idempotencyKey, settings.IdempotencyWindowSeconds, stoppingToken);
                await tradeCooldownService.MarkTradeExecutedAsync(symbol, stoppingToken);
                logger.LogInformation(
                    "DecisionWorker execution succeeded: CorrelationId={CorrelationId}, DecisionId={DecisionId}, IdempotencyKey={IdempotencyKey}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Action={Action}, Side={Side}, IsInCooldown={IsInCooldown}, CooldownRemainingSeconds={CooldownRemainingSeconds}, IdempotencyDuplicate={IdempotencyDuplicate}, LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}, ExecutionState={ExecutionState}",
                    correlationId, decisionId, idempotencyKey, symbol, rawSignal, tradingMode, executionIntent, decision.Action, executionSide, cooldown.IsInCooldown, cooldown.RemainingSeconds, false, executionResult.LocalOrderId, executionResult.ExchangeOrderId, "Executed");
                executionDesicion.ExecutionSuccess = executionResult.Success;
                executionDesicion.LocalOrderId = executionResult.LocalOrderId;
                executionDesicion.ExchangeOrderId = executionResult.ExchangeOrderId;
                executionDesicion.DecisionStatus = DecisionStatus.Executed;
                executionDesicion.GuardStage = GuardStage.Execution;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return TerminalMetrics("Execution", "Executed");
            }

            logger.LogWarning(
                "DecisionWorker execution failed: CorrelationId={CorrelationId}, DecisionId={DecisionId}, IdempotencyKey={IdempotencyKey}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Action={Action}, Side={Side}, IsInCooldown={IsInCooldown}, CooldownRemainingSeconds={CooldownRemainingSeconds}, IdempotencyDuplicate={IdempotencyDuplicate}, Error={Error}, ExecutionState={ExecutionState}",
                correlationId, decisionId, idempotencyKey, symbol, rawSignal, tradingMode, executionIntent, decision.Action, executionSide, cooldown.IsInCooldown, cooldown.RemainingSeconds, false, executionResult.Error, "Failed");
            executionDesicion.ExecutionError = executionResult.Error;
            executionDesicion.DecisionStatus = DecisionStatus.Failed;
            executionDesicion.GuardStage = GuardStage.Execution;
            await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
            return TerminalMetrics("Execution", executionResult.Error ?? "Execution failed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "DecisionWorker symbol pipeline failed: CorrelationId={CorrelationId}, Symbol={Symbol}",
                correlationId,
                symbol);
            return new SymbolProcessingMetrics(
                decisionServiceMs,
                strategyMs,
                decisionPersistMs,
                TradeSignal.Hold,
                $"Symbol pipeline exception: {ex.Message}");
        }
    }

    private async Task<CycleObservabilitySnapshot> ProcessRankedSymbolsAsync(
        DecisionWorkerSettings settings,
        string correlationId,
        CancellationToken stoppingToken,
        TradeDecisionService tradeDecisionService,
        IRiskManagementService riskManagementService,
        ITradeExecutionService tradeExecutionService,
        ITradeCooldownService tradeCooldownService,
        ITradeIdempotencyService tradeIdempotencyService,
        ITradeExecutionDesicionsRepository tradeExecutionDesicionsRepository,
        IPositionExecutionGuard positionExecutionGuard,
        IConfidenceGate confidenceGate,
        IPositionRepository positionRepository,
        ISpotPositionSizingService spotPositionSizingService)
    {
        var evaluated = new List<SymbolOpportunity>(settings.Symbols.Count);
        var evaluationStarts = new Dictionary<TradingSymbol, DateTime>(settings.Symbols.Count);
        var quantityResolveDurations = new Dictionary<TradingSymbol, long>(settings.Symbols.Count);
        var decisionServiceDurations = new Dictionary<TradingSymbol, long>(settings.Symbols.Count);
        foreach (var symbol in settings.Symbols)
        {
            var symbolEvalStartUtc = DateTime.UtcNow;
            evaluationStarts[symbol] = symbolEvalStartUtc;
            var quantityTimer = Stopwatch.StartNew();
            var sizingResult = await ResolveOpenLongQuantityAsync(
                symbol,
                settings,
                spotPositionSizingService,
                stoppingToken);
            quantityTimer.Stop();
            quantityResolveDurations[symbol] = quantityTimer.ElapsedMilliseconds;
            if (!sizingResult.IsSuccess)
            {
                logger.LogWarning(
                    "DecisionWorker skipped symbol opportunity due to position sizing rejection: Symbol={Symbol}, Reason={Reason}, QuantitySource={QuantitySource}",
                    symbol,
                    sizingResult.Reason,
                    sizingResult.QuantitySource);
                evaluated.Add(new SymbolOpportunity(
                    symbol,
                    0m,
                    false,
                    BuildSizingRejectedHoldDecision(symbol, sizingResult.Reason ?? "Position sizing rejected."),
                    0m,
                    false,
                    sizingResult.Reason));
                logger.LogInformation(
                    "DecisionWorker symbol evaluation timing: CorrelationId={CorrelationId}, Symbol={Symbol}, SymbolEvalStartUtc={SymbolEvalStartUtc}, SymbolEvalEndUtc={SymbolEvalEndUtc}, SymbolElapsedMs={SymbolElapsedMs}, QuantityResolveMs={QuantityResolveMs}, DecisionServiceMs={DecisionServiceMs}, StrategyMs={StrategyMs}, DecisionPersistMs={DecisionPersistMs}, FinalAction={FinalAction}, FinalReason={FinalReason}",
                    correlationId,
                    symbol,
                    symbolEvalStartUtc,
                    DateTime.UtcNow,
                    quantityTimer.ElapsedMilliseconds,
                    quantityTimer.ElapsedMilliseconds,
                    0L,
                    null,
                    null,
                    TradeSignal.Hold,
                    sizingResult.Reason ?? "Position sizing rejected.");
                continue;
            }

            var requestedQuantity = sizingResult.Quantity;
            var hasSymbolOverride = sizingResult.QuantitySource == SpotQuantitySource.SymbolOverride;
            var decisionTimer = Stopwatch.StartNew();
            var decision = await tradeDecisionService.MakeDecision(
                symbol,
                requestedQuantity,
                stoppingToken,
                allowStateMutation: false);
            decisionTimer.Stop();
            decisionServiceDurations[symbol] = decisionTimer.ElapsedMilliseconds;
            var score = CalculateOpportunityScore(decision);
            var isEntryCandidate = decision.Action == TradeSignal.Buy
                                   && (decision.Candidate?.ExecutionIntent ?? decision.ExecutionIntent) == TradeExecutionIntent.OpenLong
                                   && (decision.Candidate?.TradingMode ?? decision.TradingMode) == TradingMode.Spot;
            var rejectionReason = isEntryCandidate && score >= settings.MinOpportunityScore
                ? null
                : decision.Reason;
            evaluated.Add(new SymbolOpportunity(symbol, requestedQuantity, hasSymbolOverride, decision, score, isEntryCandidate, rejectionReason));

            logger.LogInformation(
                "DecisionWorker symbol opportunity evaluated: Symbol={Symbol}, RequestedQuantity={RequestedQuantity}, HasSymbolOverride={HasSymbolOverride}, QuantitySource={QuantitySource}, Action={Action}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, TrendConfidence={TrendConfidence}, MarketConditionScore={MarketConditionScore}, ExpectedTargetPrice={ExpectedTargetPrice}, ExpectedMovePercent={ExpectedMovePercent}, ExpectedTargetSource={ExpectedTargetSource}, FeeProfitGuardStatus={FeeProfitGuardStatus}, RejectionReason={RejectionReason}, OpportunityScore={OpportunityScore}, MinOpportunityScore={MinOpportunityScore}, AvailableQuoteBalance={AvailableQuoteBalance}, ReservedQuoteBalance={ReservedQuoteBalance}, UsableQuoteBalance={UsableQuoteBalance}, DesiredQuoteAmount={DesiredQuoteAmount}, CappedQuoteAmount={CappedQuoteAmount}, FinalNotional={FinalNotional}, MinNotional={MinNotional}",
                symbol,
                requestedQuantity,
                hasSymbolOverride,
                sizingResult.QuantitySource,
                decision.Action,
                decision.Candidate?.TradingMode ?? decision.TradingMode,
                decision.Candidate?.ExecutionIntent ?? decision.ExecutionIntent,
                decision.TrendConfidenceScore,
                decision.MarketConditionScore,
                decision.Candidate?.ExpectedTargetPrice,
                decision.Candidate?.ExpectedMovePercent,
                decision.Candidate?.ExpectedTargetSource,
                "PendingExecution",
                rejectionReason,
                score,
                settings.MinOpportunityScore,
                sizingResult.AvailableQuoteBalance,
                sizingResult.ReservedQuoteBalance,
                sizingResult.UsableQuoteBalance,
                sizingResult.DesiredQuoteAmount,
                sizingResult.CappedQuoteAmount,
                sizingResult.FinalNotional,
                sizingResult.MinNotional);
            logger.LogInformation(
                "DecisionWorker symbol evaluation timing: CorrelationId={CorrelationId}, Symbol={Symbol}, SymbolEvalStartUtc={SymbolEvalStartUtc}, SymbolEvalEndUtc={SymbolEvalEndUtc}, SymbolElapsedMs={SymbolElapsedMs}, QuantityResolveMs={QuantityResolveMs}, DecisionServiceMs={DecisionServiceMs}, StrategyMs={StrategyMs}, DecisionPersistMs={DecisionPersistMs}, FinalAction={FinalAction}, FinalReason={FinalReason}",
                correlationId,
                symbol,
                symbolEvalStartUtc,
                DateTime.UtcNow,
                quantityTimer.ElapsedMilliseconds + decisionTimer.ElapsedMilliseconds,
                quantityTimer.ElapsedMilliseconds,
                decisionTimer.ElapsedMilliseconds,
                null,
                null,
                decision.Action,
                decision.Reason);
        }

        var rankedEntryCandidates = evaluated
            .Where(x => x.IsEntryCandidate && x.OpportunityScore >= settings.MinOpportunityScore)
            .OrderByDescending(x => x.OpportunityScore)
            .ThenBy(x => x.Symbol.ToString(), StringComparer.Ordinal)
            .ToList();

        var openPositions = await positionRepository.GetOpenPositionsAsync(stoppingToken);
        var openCount = openPositions.Count(p => p.IsOpen);
        var availableOpenSlots = Math.Max(0, settings.GlobalMaxOpenPositions - openCount);
        var selectedSymbolsByRanking = SelectRankedEntrySymbols(
            evaluated.ToDictionary(x => x.Symbol, x => x.Decision),
            settings.MinOpportunityScore,
            settings.MaxSymbolsToTradePerCycle,
            availableOpenSlots);
        var selectedEntries = rankedEntryCandidates
            .Where(x => selectedSymbolsByRanking.Contains(x.Symbol))
            .ToList();
        var selectedSymbols = selectedEntries.Select(x => x.Symbol).ToHashSet();

        logger.LogInformation(
            "DecisionWorker symbol ranking result: TotalSymbols={TotalSymbols}, RankedEntryCandidates={RankedEntryCandidates}, SelectedEntries={SelectedEntries}, MaxSymbolsToTradePerCycle={MaxSymbolsToTradePerCycle}, GlobalMaxOpenPositions={GlobalMaxOpenPositions}, CurrentOpenPositions={CurrentOpenPositions}, AvailableOpenSlots={AvailableOpenSlots}, SelectedSymbols={SelectedSymbols}",
            settings.Symbols.Count,
            rankedEntryCandidates.Count,
            selectedEntries.Count,
            settings.MaxSymbolsToTradePerCycle,
            settings.GlobalMaxOpenPositions,
            openCount,
            availableOpenSlots,
            string.Join(",", selectedEntries.Select(x => x.Symbol)));

        foreach (var candidate in rankedEntryCandidates.Select((x, i) => new { Candidate = x, Rank = i + 1 }))
        {
            logger.LogInformation(
                "DecisionWorker symbol rank: Symbol={Symbol}, Rank={Rank}, OpportunityScore={OpportunityScore}, Selected={Selected}, ExpectedMovePercent={ExpectedMovePercent}, Reason={Reason}",
                candidate.Candidate.Symbol,
                candidate.Rank,
                candidate.Candidate.OpportunityScore,
                selectedSymbols.Contains(candidate.Candidate.Symbol),
                candidate.Candidate.Decision.Candidate?.ExpectedMovePercent,
                candidate.Candidate.Decision.Reason);
        }

        foreach (var opportunity in evaluated)
        {
            var decision = opportunity.Decision;
            var executionIntent = decision.Candidate?.ExecutionIntent ?? decision.ExecutionIntent;
            var shouldAlwaysProcess = decision.Action == TradeSignal.Sell
                                      && executionIntent == TradeExecutionIntent.CloseLong;
            var shouldProcessSelectedEntry = opportunity.IsEntryCandidate && selectedSymbols.Contains(opportunity.Symbol);
            if (shouldAlwaysProcess || shouldProcessSelectedEntry)
            {
                var metrics = await ProcessSymbolAsync(
                    opportunity.Symbol,
                    opportunity.RequestedQuantity,
                    settings,
                    correlationId,
                    stoppingToken,
                    tradeDecisionService,
                    riskManagementService,
                    tradeExecutionService,
                    tradeCooldownService,
                    tradeIdempotencyService,
                    tradeExecutionDesicionsRepository,
                    positionExecutionGuard,
                    confidenceGate,
                    spotPositionSizingService,
                    decision);
                logger.LogInformation(
                    "DecisionWorker symbol evaluation timing: CorrelationId={CorrelationId}, Symbol={Symbol}, SymbolEvalStartUtc={SymbolEvalStartUtc}, SymbolEvalEndUtc={SymbolEvalEndUtc}, SymbolElapsedMs={SymbolElapsedMs}, QuantityResolveMs={QuantityResolveMs}, DecisionServiceMs={DecisionServiceMs}, StrategyMs={StrategyMs}, DecisionPersistMs={DecisionPersistMs}, FinalAction={FinalAction}, FinalReason={FinalReason}",
                    correlationId,
                    opportunity.Symbol,
                    evaluationStarts.TryGetValue(opportunity.Symbol, out var evalStartUtc) ? evalStartUtc : DateTime.UtcNow,
                    DateTime.UtcNow,
                    evaluationStarts.TryGetValue(opportunity.Symbol, out var fullStartUtc)
                        ? (long)(DateTime.UtcNow - fullStartUtc).TotalMilliseconds
                        : 0L,
                    quantityResolveDurations.GetValueOrDefault(opportunity.Symbol),
                    decisionServiceDurations.GetValueOrDefault(opportunity.Symbol),
                    metrics.StrategyMs,
                    metrics.DecisionPersistMs,
                    metrics.FinalAction,
                    metrics.FinalReason);
                continue;
            }

            if (opportunity.IsEntryCandidate)
            {
                logger.LogInformation(
                    "DecisionWorker symbol skipped by ranking: Symbol={Symbol}, OpportunityScore={OpportunityScore}, MinOpportunityScore={MinOpportunityScore}, Selected={Selected}, Reason={Reason}",
                    opportunity.Symbol,
                    opportunity.OpportunityScore,
                    settings.MinOpportunityScore,
                    false,
                    "Not in selected ranked opportunities.");
                logger.LogInformation(
                    "DecisionWorker symbol evaluation timing: CorrelationId={CorrelationId}, Symbol={Symbol}, SymbolEvalStartUtc={SymbolEvalStartUtc}, SymbolEvalEndUtc={SymbolEvalEndUtc}, SymbolElapsedMs={SymbolElapsedMs}, QuantityResolveMs={QuantityResolveMs}, DecisionServiceMs={DecisionServiceMs}, StrategyMs={StrategyMs}, DecisionPersistMs={DecisionPersistMs}, FinalAction={FinalAction}, FinalReason={FinalReason}",
                    correlationId,
                    opportunity.Symbol,
                    evaluationStarts.TryGetValue(opportunity.Symbol, out var evalStartUtc) ? evalStartUtc : DateTime.UtcNow,
                    DateTime.UtcNow,
                    evaluationStarts.TryGetValue(opportunity.Symbol, out var fullStartUtc)
                        ? (long)(DateTime.UtcNow - fullStartUtc).TotalMilliseconds
                        : 0L,
                    quantityResolveDurations.GetValueOrDefault(opportunity.Symbol),
                    decisionServiceDurations.GetValueOrDefault(opportunity.Symbol),
                    null,
                    null,
                    TradeSignal.Hold,
                    "Execution skipped by symbol ranking layer.");
            }
            else if (decision.Action != TradeSignal.Hold)
            {
                var metrics = await ProcessSymbolAsync(
                    opportunity.Symbol,
                    opportunity.RequestedQuantity,
                    settings,
                    correlationId,
                    stoppingToken,
                    tradeDecisionService,
                    riskManagementService,
                    tradeExecutionService,
                    tradeCooldownService,
                    tradeIdempotencyService,
                    tradeExecutionDesicionsRepository,
                    positionExecutionGuard,
                    confidenceGate,
                    spotPositionSizingService,
                    BuildRankingSkippedHoldDecision(decision, "Execution skipped by symbol ranking layer."));
                logger.LogInformation(
                    "DecisionWorker symbol evaluation timing: CorrelationId={CorrelationId}, Symbol={Symbol}, SymbolEvalStartUtc={SymbolEvalStartUtc}, SymbolEvalEndUtc={SymbolEvalEndUtc}, SymbolElapsedMs={SymbolElapsedMs}, QuantityResolveMs={QuantityResolveMs}, DecisionServiceMs={DecisionServiceMs}, StrategyMs={StrategyMs}, DecisionPersistMs={DecisionPersistMs}, FinalAction={FinalAction}, FinalReason={FinalReason}",
                    correlationId,
                    opportunity.Symbol,
                    evaluationStarts.TryGetValue(opportunity.Symbol, out var evalStartUtc) ? evalStartUtc : DateTime.UtcNow,
                    DateTime.UtcNow,
                    evaluationStarts.TryGetValue(opportunity.Symbol, out var fullStartUtc)
                        ? (long)(DateTime.UtcNow - fullStartUtc).TotalMilliseconds
                        : 0L,
                    quantityResolveDurations.GetValueOrDefault(opportunity.Symbol),
                    decisionServiceDurations.GetValueOrDefault(opportunity.Symbol),
                    metrics.StrategyMs,
                    metrics.DecisionPersistMs,
                    metrics.FinalAction,
                    metrics.FinalReason);
            }
            else
            {
                var metrics = await ProcessSymbolAsync(
                    opportunity.Symbol,
                    opportunity.RequestedQuantity,
                    settings,
                    correlationId,
                    stoppingToken,
                    tradeDecisionService,
                    riskManagementService,
                    tradeExecutionService,
                    tradeCooldownService,
                    tradeIdempotencyService,
                    tradeExecutionDesicionsRepository,
                    positionExecutionGuard,
                    confidenceGate,
                    spotPositionSizingService,
                    decision);
                logger.LogInformation(
                    "DecisionWorker symbol evaluation timing: CorrelationId={CorrelationId}, Symbol={Symbol}, SymbolEvalStartUtc={SymbolEvalStartUtc}, SymbolEvalEndUtc={SymbolEvalEndUtc}, SymbolElapsedMs={SymbolElapsedMs}, QuantityResolveMs={QuantityResolveMs}, DecisionServiceMs={DecisionServiceMs}, StrategyMs={StrategyMs}, DecisionPersistMs={DecisionPersistMs}, FinalAction={FinalAction}, FinalReason={FinalReason}",
                    correlationId,
                    opportunity.Symbol,
                    evaluationStarts.TryGetValue(opportunity.Symbol, out var evalStartUtc) ? evalStartUtc : DateTime.UtcNow,
                    DateTime.UtcNow,
                    evaluationStarts.TryGetValue(opportunity.Symbol, out var fullStartUtc)
                        ? (long)(DateTime.UtcNow - fullStartUtc).TotalMilliseconds
                        : 0L,
                    quantityResolveDurations.GetValueOrDefault(opportunity.Symbol),
                    decisionServiceDurations.GetValueOrDefault(opportunity.Symbol),
                    metrics.StrategyMs,
                    metrics.DecisionPersistMs,
                    metrics.FinalAction,
                    metrics.FinalReason);
            }
        }

        return new CycleObservabilitySnapshot(
            selectedEntries.Select(x => x.Symbol).ToArray(),
            openCount,
            availableOpenSlots);
    }

    private static async Task<SpotPositionSizingResult> ResolveOpenLongQuantityAsync(
        TradingSymbol symbol,
        DecisionWorkerSettings settings,
        ISpotPositionSizingService spotPositionSizingService,
        CancellationToken cancellationToken)
    {
        return await spotPositionSizingService.ResolveOpenLongQuantityAsync(
            new SpotPositionSizingRequest
            {
                Symbol = symbol,
                GlobalQuantity = settings.Quantity,
                SymbolQuantities = settings.SymbolQuantities
            },
            cancellationToken);
    }

    private static DecisionResult BuildSizingRejectedHoldDecision(TradingSymbol symbol, string reason)
    {
        return new DecisionResult
        {
            Action = TradeSignal.Hold,
            StrategyName = "PositionSizing",
            RawSignal = TradeSignal.Hold,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Reason = $"Position sizing rejected for {symbol}: {reason}",
            Confidence = 0m
        };
    }

    private static decimal CalculateOpportunityScore(DecisionResult decision)
    {
        if (decision.Action != TradeSignal.Buy)
            return 0m;

        var baseScore = Math.Clamp(decision.Confidence, 0m, 1m) * 100m;
        var expectedMoveBoost = Math.Max(0m, decision.Candidate?.ExpectedMovePercent ?? 0m);
        var trendConfidenceBoost = Math.Max(0m, decision.TrendConfidenceScore ?? 0) * 0.2m;
        var marketConditionBoost = Math.Max(0m, decision.MarketConditionScore ?? 0) * 0.1m;
        return Math.Round(baseScore + expectedMoveBoost + trendConfidenceBoost + marketConditionBoost, 4);
    }

    private static IReadOnlyList<TradingSymbol> SelectRankedEntrySymbols(
        IReadOnlyDictionary<TradingSymbol, DecisionResult> decisionsBySymbol,
        decimal minOpportunityScore,
        int maxSymbolsToTradePerCycle,
        int availableOpenSlots)
    {
        var maxEntriesToExecute = Math.Max(0, Math.Min(maxSymbolsToTradePerCycle, availableOpenSlots));
        if (maxEntriesToExecute == 0 || decisionsBySymbol.Count == 0)
            return [];

        return decisionsBySymbol
            .Where(x =>
            {
                var executionIntent = x.Value.Candidate?.ExecutionIntent ?? x.Value.ExecutionIntent;
                var tradingMode = x.Value.Candidate?.TradingMode ?? x.Value.TradingMode;
                return x.Value.Action == TradeSignal.Buy
                       && tradingMode == TradingMode.Spot
                       && executionIntent == TradeExecutionIntent.OpenLong
                       && CalculateOpportunityScore(x.Value) >= minOpportunityScore;
            })
            .OrderByDescending(x => CalculateOpportunityScore(x.Value))
            .ThenBy(x => x.Key.ToString(), StringComparer.Ordinal)
            .Take(maxEntriesToExecute)
            .Select(x => x.Key)
            .ToArray();
    }

    private static DecisionResult BuildRankingSkippedHoldDecision(DecisionResult source, string reason)
    {
        return new DecisionResult
        {
            StrategyName = source.StrategyName,
            Action = TradeSignal.Hold,
            RawSignal = source.RawSignal,
            TradingMode = source.TradingMode,
            ExecutionIntent = source.ExecutionIntent,
            Reason = reason,
            Candidate = source.Candidate,
            Confidence = source.Confidence,
            TrendConfidenceScore = source.TrendConfidenceScore,
            MarketConditionScore = source.MarketConditionScore
        };
    }

    private DecisionWorkerSettings ReadSettings()
    {
        var decision = RuntimeTradingConfigResolver.ResolveDecisionEngine(configuration);
        var execution = RuntimeTradingConfigResolver.ResolveExecution(configuration);
        var cooldownSeconds = Math.Max(0,
            configuration.GetValue<int?>("Trading:CooldownSeconds")
            ?? decision.TradeCooldownSeconds);

        var configuredSymbols = decision.Symbols?.ToArray() ?? [];
        var parsedSymbols = configuredSymbols
            .Select(x => Enum.TryParse<TradingSymbol>(x, true, out var symbol) ? symbol : (TradingSymbol?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        if (parsedSymbols.Count == 0)
        {
            var symbolText = decision.Symbol ?? DefaultSymbol;
            var symbol = Enum.TryParse<TradingSymbol>(symbolText, true, out var parsed) ? parsed : TradingSymbol.BTCUSDT;
            parsedSymbols.Add(symbol);
        }

        var symbolQuantities = new Dictionary<TradingSymbol, decimal>();
        foreach (var pair in decision.SymbolQuantities)
        {
            if (!Enum.TryParse<TradingSymbol>(pair.Key, true, out var symbol))
            {
                logger.LogWarning(
                    "DecisionWorker ignored invalid symbol quantity key. Symbol={SymbolKey}",
                    pair.Key);
                continue;
            }

            var symbolQuantity = pair.Value;

            if (symbolQuantity <= 0m)
            {
                logger.LogWarning(
                    "DecisionWorker ignored non-positive symbol quantity. Symbol={Symbol}, Quantity={Quantity}",
                    symbol,
                    symbolQuantity);
                continue;
            }

            symbolQuantities[symbol] = symbolQuantity;
        }

        return new DecisionWorkerSettings
        {
            IntervalSeconds = decision.IntervalSeconds,
            Quantity = decision.Quantity,
            Symbols = parsedSymbols,
            SymbolQuantities = symbolQuantities,
            ExecutionEnabled = execution.Enabled,
            UseMarketOrders = execution.UseMarketOrders,
            MinExecutionConfidence = Math.Clamp(decision.MinExecutionConfidence, 0m, 1m),
            TradeCooldownSeconds = cooldownSeconds,
            IdempotencyWindowSeconds = decision.IdempotencyWindowSeconds,
            EnableSymbolRanking = decision.EnableSymbolRanking,
            MaxSymbolsToTradePerCycle = decision.MaxSymbolsToTradePerCycle,
            MinOpportunityScore = decision.MinOpportunityScore,
            GlobalMaxOpenPositions = RuntimeTradingConfigResolver.ResolveRisk(configuration).MaxOpenPositions
        };
    }

    private static decimal ResolveQuantityForSymbol(TradingSymbol symbol, DecisionWorkerSettings settings)
    {
        if (settings.SymbolQuantities.TryGetValue(symbol, out var symbolQuantity) && symbolQuantity > 0m)
            return symbolQuantity;

        return settings.Quantity;
    }

    private static OrderSide? InferSideFromAction(TradeSignal action)
    {
        return action switch
        {
            TradeSignal.Buy => OrderSide.BUY,
            TradeSignal.Sell => OrderSide.SELL,
            _ => null
        };
    }

    private static string CreateDecisionId(
        string correlationId,
        TradingSymbol symbol,
        TradeSignal action,
        OrderSide? side,
        decimal quantity,
        decimal price)
    {
        var raw = $"{correlationId}|{symbol}|{action}|{side}|{quantity:F8}|{price:F8}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..32];
    }

    private static string CreateIdempotencyKey(
        TradingSymbol symbol,
        TradeSignal action,
        OrderSide? side,
        decimal quantity,
        int idempotencyWindowSeconds)
    {
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Math.Max(1, idempotencyWindowSeconds);
        var raw = $"{symbol}|{action}|{side}|{quantity:F8}|{bucket}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..32];
    }

    private sealed class DecisionWorkerSettings
    {
        public int IntervalSeconds { get; init; }
        public decimal Quantity { get; init; }
        public required IReadOnlyList<TradingSymbol> Symbols { get; init; }
        public required IReadOnlyDictionary<TradingSymbol, decimal> SymbolQuantities { get; init; }
        public bool ExecutionEnabled { get; init; }
        public bool UseMarketOrders { get; init; }
        public decimal MinExecutionConfidence { get; init; }
        public int TradeCooldownSeconds { get; init; }
        public int IdempotencyWindowSeconds { get; init; }
        public bool EnableSymbolRanking { get; init; }
        public int MaxSymbolsToTradePerCycle { get; init; }
        public decimal MinOpportunityScore { get; init; }
        public int GlobalMaxOpenPositions { get; init; }
    }

    private sealed record SymbolProcessingMetrics(
        long DecisionServiceMs,
        decimal? StrategyMs,
        long? DecisionPersistMs,
        TradeSignal FinalAction,
        string FinalReason);

    private sealed record CycleObservabilitySnapshot(
        IReadOnlyList<TradingSymbol> SelectedSymbols,
        int CurrentOpenPositions,
        int AvailableOpenSlots);

    private sealed record SymbolOpportunity(
        TradingSymbol Symbol,
        decimal RequestedQuantity,
        bool HasSymbolOverride,
        DecisionResult Decision,
        decimal OpportunityScore,
        bool IsEntryCandidate,
        string? RejectionReason);
}
