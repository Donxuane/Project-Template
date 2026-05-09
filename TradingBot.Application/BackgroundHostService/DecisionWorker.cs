using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

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
            "DecisionWorker started. Symbols={Symbols}, Quantity={Quantity}, Interval={IntervalSeconds}s, MinExecutionConfidence={MinExecutionConfidence}, TradeCooldownSeconds={TradeCooldownSeconds}, IdempotencyWindowSeconds={IdempotencyWindowSeconds}, ExecutionEnabled={ExecutionEnabled}, UseMarketOrders={UseMarketOrders}",
            string.Join(",", settings.Symbols), settings.Quantity, settings.IntervalSeconds, settings.MinExecutionConfidence, settings.TradeCooldownSeconds, settings.IdempotencyWindowSeconds, settings.ExecutionEnabled, settings.UseMarketOrders);

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
                using (var scope = scopeFactory.CreateScope())
                {
                    var tradeDecisionService = scope.ServiceProvider.GetRequiredService<TradeDecisionService>();
                    var riskManagementService = scope.ServiceProvider.GetRequiredService<IRiskManagementService>();
                    var tradeExecutionService = scope.ServiceProvider.GetRequiredService<ITradeExecutionService>();
                    var tradeCooldownService = scope.ServiceProvider.GetRequiredService<ITradeCooldownService>();
                    var tradeIdempotencyService = scope.ServiceProvider.GetRequiredService<ITradeIdempotencyService>();
                    var tradeExecutionDesicionsRepository = scope.ServiceProvider.GetRequiredService<ITradeExecutionDesicionsRepository>();
                    var positionExecutionGuard = scope.ServiceProvider.GetRequiredService<IPositionExecutionGuard>();
                    var feeProfitGuard = scope.ServiceProvider.GetRequiredService<IFeeProfitGuard>();
                    var confidenceGate = scope.ServiceProvider.GetRequiredService<IConfidenceGate>();
                    foreach (var symbol in settings.Symbols)
                    {
                        await ProcessSymbolAsync(
                            symbol,
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
                            feeProfitGuard,
                            confidenceGate);
                    }
                }

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

    private async Task ProcessSymbolAsync(
        TradingSymbol symbol,
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
        IFeeProfitGuard feeProfitGuard,
        IConfidenceGate confidenceGate
        )
    {
        try
        {
            var decision = await tradeDecisionService.MakeDecision(symbol, settings.Quantity, stoppingToken);
            var side = decision.Candidate?.Side ?? InferSideFromAction(decision.Action);
            var rawSignal = decision.RawSignal == default && decision.Action != TradeSignal.Hold
                ? decision.Action
                : decision.RawSignal;
            var tradingMode = decision.Candidate?.TradingMode ?? decision.TradingMode;
            var executionIntent = decision.Candidate?.ExecutionIntent ?? decision.ExecutionIntent;
            var strategyName = string.IsNullOrWhiteSpace(decision.StrategyName) ? "Unknown" : decision.StrategyName;
            var executionQuantity = decision.Candidate?.Quantity ?? settings.Quantity;
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
                MinConfidence = settings.MinExecutionConfidence
            };


            var executionId = await tradeExecutionDesicionsRepository.AddDesicionAsync(executionDesicion);
            
            if (decision.Action == TradeSignal.Hold)
            {
                executionDesicion.ExecutionError = "Execution skipped - hold action.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

            if (decision.Action != TradeSignal.Buy && decision.Action != TradeSignal.Sell)
            {
                executionDesicion.ExecutionError = "Execution skipped - unsupported action.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

            if (!settings.ExecutionEnabled)
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Side={Side}, Quantity={Quantity}, SkipReason={SkipReason}",
                    correlationId, decisionId, symbol, rawSignal, tradingMode, executionIntent, side, executionQuantity, "SafeModeDisabled");
                executionDesicion.ExecutionError = "Execution skipped - execution is disabled.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

            if (!settings.UseMarketOrders)
            {
                logger.LogWarning(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Side={Side}, Quantity={Quantity}, SkipReason={SkipReason}",
                    correlationId, decisionId, symbol, rawSignal, tradingMode, executionIntent, side, executionQuantity, "OnlyMarketOrdersSupported");
                executionDesicion.ExecutionError = "Execution skipped - only market orders are supported.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

            if (decision.Candidate is null || decision.Candidate.Price.GetValueOrDefault() <= 0m)
            {
                executionDesicion.ExecutionError = "Execution skipped - invalid trade candidate.";
                executionDesicion.RiskIsAllowed = false;
                executionDesicion.RiskReason = "Execution skipped - invalid trade candidate.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
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
                return;
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
                return;
            }

            if (!await tradeIdempotencyService.TryRegisterDecisionAsync(idempotencyKey, settings.IdempotencyWindowSeconds, stoppingToken))
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, IdempotencyKey={IdempotencyKey}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Action={Action}, Side={Side}, IsInCooldown={IsInCooldown}, CooldownRemainingSeconds={CooldownRemainingSeconds}, IdempotencyDuplicate={IdempotencyDuplicate}, SkipReason={SkipReason}, ExecutionState={ExecutionState}",
                    correlationId, decisionId, idempotencyKey, symbol, rawSignal, tradingMode, executionIntent, decision.Action, executionSide, cooldown.IsInCooldown, cooldown.RemainingSeconds, true, "IdempotencyDuplicate", "Skipped");
                executionDesicion.IdempotencyDuplicate = true;
                executionDesicion.ExecutionError = "Execution skipped - idempotency duplicate.";
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                executionDesicion.GuardStage = GuardStage.Idempotency;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

            var requiresReducedPositionSize = decision.Candidate?.RequiresReducedPositionSize ?? false;
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
                return;
            }

            logger.LogInformation(
                "DecisionWorker risk approved: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, StopLossPrice={StopLossPrice}, TakeProfitPrice={TakeProfitPrice}",
                correlationId, decisionId, symbol, executionSide, riskResult.StopLossPrice, riskResult.TakeProfitPrice);

            var feeGuardResult = await feeProfitGuard.EvaluateAsync(
                new FeeProfitGuardRequest
                {
                    Symbol = symbol,
                    TradingMode = tradingMode,
                    RawSignal = rawSignal,
                    ExecutionIntent = executionIntent,
                    Side = executionSide,
                    Quantity = executionQuantity,
                    EntryPrice = price > 0m ? price : null,
                    TargetPrice = riskResult.TakeProfitPrice,
                    StopLossPrice = riskResult.StopLossPrice,
                    IsProtectiveExit = false
                },
                stoppingToken);
            if (!feeGuardResult.IsAllowed)
            {
                executionDesicion.ExecutionSuccess = false;
                executionDesicion.ExecutionError = feeGuardResult.Reason;
                executionDesicion.RiskIsAllowed = false;
                executionDesicion.RiskReason = feeGuardResult.Reason;
                executionDesicion.DecisionStatus = DecisionStatus.Skipped;
                executionDesicion.GuardStage = GuardStage.FeeProfitGuard;
                logger.LogInformation(
                    "DecisionWorker execution skipped by fee guard: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Side={Side}, Quantity={Quantity}, EntryPrice={EntryPrice}, TargetPrice={TargetPrice}, StopLossPrice={StopLossPrice}, GrossExpectedProfitPercent={GrossExpectedProfitPercent}, EstimatedEntryFeePercent={EstimatedEntryFeePercent}, EstimatedExitFeePercent={EstimatedExitFeePercent}, EstimatedSpreadPercent={EstimatedSpreadPercent}, EstimatedTotalCostPercent={EstimatedTotalCostPercent}, NetExpectedProfitPercent={NetExpectedProfitPercent}, Allowed={Allowed}, Reason={Reason}",
                    correlationId,
                    decisionId,
                    symbol,
                    tradingMode,
                    executionIntent,
                    executionSide,
                    executionQuantity,
                    feeGuardResult.EntryPrice,
                    feeGuardResult.TargetPrice,
                    feeGuardResult.StopLossPrice,
                    feeGuardResult.GrossExpectedProfitPercent,
                    feeGuardResult.EstimatedEntryFeePercent,
                    feeGuardResult.EstimatedExitFeePercent,
                    feeGuardResult.EstimatedSpreadPercent,
                    feeGuardResult.EstimatedTotalCostPercent,
                    feeGuardResult.NetExpectedProfitPercent,
                    false,
                    feeGuardResult.Reason);
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

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
                return;
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
                    ExecutionIntent = executionIntent
                },
                stoppingToken);

            if (executionResult.Success)
            {
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
                return;
            }

            logger.LogWarning(
                "DecisionWorker execution failed: CorrelationId={CorrelationId}, DecisionId={DecisionId}, IdempotencyKey={IdempotencyKey}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Action={Action}, Side={Side}, IsInCooldown={IsInCooldown}, CooldownRemainingSeconds={CooldownRemainingSeconds}, IdempotencyDuplicate={IdempotencyDuplicate}, Error={Error}, ExecutionState={ExecutionState}",
                correlationId, decisionId, idempotencyKey, symbol, rawSignal, tradingMode, executionIntent, decision.Action, executionSide, cooldown.IsInCooldown, cooldown.RemainingSeconds, false, executionResult.Error, "Failed");
            executionDesicion.ExecutionError = executionResult.Error;
            executionDesicion.DecisionStatus = DecisionStatus.Failed;
            executionDesicion.GuardStage = GuardStage.Execution;
            await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
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
        }
    }

    private DecisionWorkerSettings ReadSettings()
    {
        var intervalSeconds = Math.Max(1, configuration.GetValue<int?>("DecisionEngine:IntervalSeconds") ?? DefaultIntervalSeconds);
        var quantity = configuration.GetValue<decimal?>("DecisionEngine:Quantity") ?? DefaultQuantity;
        var executionEnabled = configuration.GetValue<bool?>("ExecutionSettings:Enabled") ?? false;
        var useMarketOrders = configuration.GetValue<bool?>("ExecutionSettings:UseMarketOrders") ?? true;
        var minExecutionConfidence = configuration.GetValue<decimal?>("DecisionEngine:MinExecutionConfidence")
                            ?? configuration.GetValue<decimal?>("DecisionEngine:MinConfidence")
                            ?? configuration.GetValue<decimal?>("DecisionEngine:MinConfidenceThreshold")
                            ?? DefaultMinExecutionConfidence;
        var cooldownSeconds = Math.Max(0,
            configuration.GetValue<int?>("Trading:CooldownSeconds")
            ?? configuration.GetValue<int?>("DecisionEngine:TradeCooldownSeconds")
            ?? DefaultTradeCooldownSeconds);
        var idempotencyWindowSeconds = Math.Max(10, configuration.GetValue<int?>("DecisionEngine:IdempotencyWindowSeconds") ?? DefaultIdempotencyWindowSeconds);

        var configuredSymbols = configuration.GetSection("DecisionEngine:Symbols").Get<string[]>() ?? [];
        var parsedSymbols = configuredSymbols
            .Select(x => Enum.TryParse<TradingSymbol>(x, true, out var symbol) ? symbol : (TradingSymbol?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        if (parsedSymbols.Count == 0)
        {
            var symbolText = configuration.GetValue<string>("DecisionEngine:Symbol") ?? DefaultSymbol;
            var symbol = Enum.TryParse<TradingSymbol>(symbolText, true, out var parsed) ? parsed : TradingSymbol.BTCUSDT;
            parsedSymbols.Add(symbol);
        }

        return new DecisionWorkerSettings
        {
            IntervalSeconds = intervalSeconds,
            Quantity = quantity,
            Symbols = parsedSymbols,
            ExecutionEnabled = executionEnabled,
            UseMarketOrders = useMarketOrders,
            MinExecutionConfidence = Math.Clamp(minExecutionConfidence, 0m, 1m),
            TradeCooldownSeconds = cooldownSeconds,
            IdempotencyWindowSeconds = idempotencyWindowSeconds
        };
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
        public bool ExecutionEnabled { get; init; }
        public bool UseMarketOrders { get; init; }
        public decimal MinExecutionConfidence { get; init; }
        public int TradeCooldownSeconds { get; init; }
        public int IdempotencyWindowSeconds { get; init; }
    }
}
