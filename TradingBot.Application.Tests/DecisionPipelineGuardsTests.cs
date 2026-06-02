using System.Data;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Application.Trading;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Binance;
using TradingBot.Domain.Models.Decision;
using TradingBot.Domain.Models.MarketData;
using TradingBot.Domain.Models.Trading;
using TradingBot.Domain.Models.TradingEndpoints;
using TradingBot.Shared.Shared.Models;
using Xunit;
using EndpointTrading = TradingBot.Domain.Enums.Endpoints.Trading;

namespace TradingBot.Application.Tests;

public class DecisionPipelineGuardsTests
{
    [Fact]
    public void DecisionId_IsUniquePerDecisionEvent_AndIdempotencyKey_IsStableWithoutPriceInput()
    {
        var createDecisionId = typeof(DecisionWorker).GetMethod("CreateDecisionId", BindingFlags.NonPublic | BindingFlags.Static);
        var createIdempotencyKey = typeof(DecisionWorker).GetMethod("CreateIdempotencyKey", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(createDecisionId);
        Assert.NotNull(createIdempotencyKey);

        var decisionId1 = (string)createDecisionId!.Invoke(null, new object[]
        {
            "corr-a",
            TradingSymbol.BNBUSDT,
            TradeSignal.Buy,
            OrderSide.BUY,
            0.01m,
            631.12m
        })!;

        var decisionId2 = (string)createDecisionId.Invoke(null, new object[]
        {
            "corr-b",
            TradingSymbol.BNBUSDT,
            TradeSignal.Buy,
            OrderSide.BUY,
            0.01m,
            631.12m
        })!;

        Assert.NotEqual(decisionId1, decisionId2);

        var idempotencyMethodParams = createIdempotencyKey!.GetParameters().Select(p => p.Name ?? string.Empty).ToArray();
        Assert.DoesNotContain(idempotencyMethodParams, p => p.Contains("price", StringComparison.OrdinalIgnoreCase));

        var idemKey1 = (string)createIdempotencyKey.Invoke(null, new object[]
        {
            TradingSymbol.BNBUSDT,
            TradeSignal.Buy,
            OrderSide.BUY,
            0.01m,
            30
        })!;
        var idemKey2 = (string)createIdempotencyKey.Invoke(null, new object[]
        {
            TradingSymbol.BNBUSDT,
            TradeSignal.Buy,
            OrderSide.BUY,
            0.01m,
            30
        })!;

        Assert.Equal(idemKey1, idemKey2);
    }

    [Fact]
    public async Task DuplicateIdempotencyKey_BlocksExecutionBeforeTradeExecutionService()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: false,
            decisionRepository: decisionRepository,
            executionService: executionService);

        Assert.Equal(0, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.True(decisionRepository.LastUpdated!.IdempotencyDuplicate);
        Assert.Contains("idempotency duplicate", decisionRepository.LastUpdated.ExecutionError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutedSpotOpenLong_PersistsEntrySnapshotFields()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();
        var decision = new DecisionResult
        {
            Action = TradeSignal.Buy,
            RawSignal = TradeSignal.Buy,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Confidence = 0.9m,
            TrendConfidenceScore = 77,
            MarketConditionScore = 66,
            Reason = "snapshot-test",
            Candidate = new TradeCandidate
            {
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.01m,
                Price = 631m,
                RawSignal = TradeSignal.Buy,
                TradingMode = TradingMode.Spot,
                ExecutionIntent = TradeExecutionIntent.OpenLong,
                ExpectedMovePercent = 0.35m,
                ExpectedTargetPrice = 640m,
                ExpectedTargetSource = "MovingAverageTrendStrategy.NormalTrendExpectedTarget",
                TrendConfidenceScore = 77,
                MarketConditionScore = 66,
                VolatilityRegime = "Low",
                RequiresReducedPositionSize = true,
                ConsecutiveBullishTrendCandles = 3,
                CurrentCloseAboveRecentHigh = true,
                DistanceToInvalidationPercent = 0.42m,
                PreviousCandleBearish = false,
                EntryNearRecentHigh = true,
                ShortMaSlopePercent = 0.0007m,
                TrendStrengthPercent = 0.0011m,
                ProjectionMode = "MaxAtrStructure",
                ProjectedExtension = 2.15m
            }
        };

        await InvokeProcessSymbolAsync(
            decisionResult: decision,
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService);

        Assert.Equal(1, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastAdded);
        Assert.Equal(0.35m, decisionRepository.LastAdded!.ExpectedMovePercent);
        Assert.Equal(640m, decisionRepository.LastAdded.ExpectedTargetPrice);
        Assert.Equal("MovingAverageTrendStrategy.NormalTrendExpectedTarget", decisionRepository.LastAdded.ExpectedTargetSource);
        Assert.Equal(77, decisionRepository.LastAdded.TrendConfidenceScore);
        Assert.Equal(66, decisionRepository.LastAdded.MarketConditionScore);
        Assert.Equal("Low", decisionRepository.LastAdded.VolatilityRegime);
        Assert.True(decisionRepository.LastAdded.RequiresReducedPositionSize);
        Assert.Equal(3, decisionRepository.LastAdded.ConsecutiveBullishTrendCandles);
        Assert.True(decisionRepository.LastAdded.CurrentCloseAboveRecentHigh);
        Assert.Equal(0.42m, decisionRepository.LastAdded.DistanceToInvalidationPercent);
        Assert.False(decisionRepository.LastAdded.PreviousCandleBearish);
        Assert.True(decisionRepository.LastAdded.EntryNearRecentHigh);
        Assert.Equal(0.0007m, decisionRepository.LastAdded.ShortMaSlopePercent);
        Assert.Equal(0.0011m, decisionRepository.LastAdded.TrendStrengthPercent);
        Assert.Equal("MaxAtrStructure", decisionRepository.LastAdded.ProjectionMode);
        Assert.Equal(2.15m, decisionRepository.LastAdded.ProjectedExtension);
    }

    [Fact]
    public async Task HoldAndSell_DecisionsAllowNullEntrySnapshotFields()
    {
        var holdRepository = new FakeTradeExecutionDecisionsRepository();
        var sellRepository = new FakeTradeExecutionDecisionsRepository();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Hold),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: holdRepository,
            executionService: new FakeTradeExecutionService());

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Sell),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: sellRepository,
            executionService: new FakeTradeExecutionService(),
            positionExecutionGuard: new FakePositionExecutionGuard(new PositionExecutionGuardResult
            {
                IsAllowed = true,
                Reason = "ok",
                OpenPositionQuantity = 0.01m
            }));

        Assert.NotNull(holdRepository.LastAdded);
        Assert.Null(holdRepository.LastAdded!.ExpectedMovePercent);
        Assert.Null(holdRepository.LastAdded.ExpectedTargetPrice);
        Assert.Null(holdRepository.LastAdded.ProjectionMode);

        Assert.NotNull(sellRepository.LastAdded);
        Assert.Null(sellRepository.LastAdded!.ExpectedMovePercent);
        Assert.Null(sellRepository.LastAdded.ExpectedTargetPrice);
        Assert.Null(sellRepository.LastAdded.ProjectionMode);
    }

    [Fact]
    public async Task ExecutionSuccess_MarksIdempotencyKey()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();
        var idempotencyService = new FakeTradeIdempotencyService(isDuplicate: false);

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService,
            idempotencyService: idempotencyService);

        Assert.Equal(1, executionService.ExecuteCalls);
        Assert.Equal(1, idempotencyService.MarkExecutedCalls);
        Assert.NotNull(idempotencyService.LastMarkedKey);
    }

    [Fact]
    public async Task ExecutionFailure_DoesNotMarkIdempotencyKey()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FailingTradeExecutionService("Skipped because expected gross move is below minimum threshold.");
        var idempotencyService = new FakeTradeIdempotencyService(isDuplicate: false);

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService,
            idempotencyService: idempotencyService);

        Assert.Equal(1, executionService.ExecuteCalls);
        Assert.Equal(0, idempotencyService.MarkExecutedCalls);
        Assert.Null(idempotencyService.LastMarkedKey);
    }

    [Fact]
    public async Task ExecutionFailureThenRetry_AllowsSecondAttemptInSameBucket()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var failingExecutionService = new FailingTradeExecutionService("Skipped because expected gross move is below minimum threshold.");
        var successExecutionService = new FakeTradeExecutionService();
        var idempotencyService = new FakeTradeIdempotencyService(isDuplicate: false);

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: failingExecutionService,
            idempotencyService: idempotencyService);

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: successExecutionService,
            idempotencyService: idempotencyService);

        Assert.Equal(1, failingExecutionService.ExecuteCalls);
        Assert.Equal(1, successExecutionService.ExecuteCalls);
        Assert.Equal(1, idempotencyService.MarkExecutedCalls);
    }

    [Fact]
    public async Task CooldownBlockedDecision_PersistsCooldownFields()
    {
        var lastTrade = DateTimeOffset.UtcNow.AddSeconds(-10);
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult
            {
                IsInCooldown = true,
                RemainingSeconds = 20,
                LastTradeAtUtc = lastTrade.UtcDateTime
            },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService);

        Assert.Equal(0, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.True(decisionRepository.LastUpdated!.IsInCooldown);
        Assert.Equal(20, decisionRepository.LastUpdated.CooldownRemainingSeconds);
        Assert.NotNull(decisionRepository.LastUpdated.CooldownLastTrade);
        Assert.Null(decisionRepository.LastUpdated.LocalOrderId);
    }

    [Fact]
    public async Task NonCooldownDecision_PersistsIsInCooldownFalse()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: false,
            decisionRepository: decisionRepository,
            executionService: executionService);

        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.False(decisionRepository.LastUpdated!.IsInCooldown);
        Assert.Equal(0, decisionRepository.LastUpdated.CooldownRemainingSeconds);
    }

    [Fact]
    public async Task SuccessfulPlaceSpotOrder_MarksCooldownForNonDecisionPath()
    {
        var cooldownService = new FakeTradeCooldownService(new CooldownCheckResult());
        var orderRepository = new FakeOrderRepository();
        var tradeExecutionRepository = new FakeTradeExecutionRepository();
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(new FakeBinanceClientService(), new FakeBinanceEndpointsService()),
            orderRepository,
            new FakePositionRepository(),
            tradeExecutionRepository,
            new FakeOrderStatusService(),
            cooldownService,
            new FakeRiskManagementService(),
            new FakeFeeProfitGuard(new FeeProfitGuardResult { IsAllowed = true, Reason = "ok" }),
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            BuildConfiguration(),
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(cooldownService.MarkedSymbols);
        Assert.Equal(TradingSymbol.BNBUSDT, cooldownService.MarkedSymbols[0]);
    }

    [Fact]
    public async Task FuturesSellIntent_IsNotExecutedBySpotExecutionPipeline()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Sell, TradingMode.Futures, TradeExecutionIntent.OpenShort),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService,
            positionExecutionGuard: new FakePositionExecutionGuard(new PositionExecutionGuardResult
            {
                IsAllowed = false,
                Reason = "Futures execution intent is not supported by the current spot execution pipeline.",
                OpenPositionQuantity = 0m
            }));

        Assert.Equal(0, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.Contains("Futures execution intent is not supported", decisionRepository.LastUpdated!.ExecutionError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotOpenLong_FeeGuardRejects_BlocksOrderPlacementAtSharedHandler()
    {
        var cooldownService = new FakeTradeCooldownService(new CooldownCheckResult());
        var orderRepository = new FakeOrderRepository();
        var tradeExecutionRepository = new FakeTradeExecutionRepository();
        var binanceClient = new FakeBinanceClientService();
        var feeGuard = new FakeFeeProfitGuard(new FeeProfitGuardResult
        {
            IsAllowed = false,
            Reason = "Skipped because expected net profit after fees/spread is below minimum threshold.",
            EntryPrice = 631m,
            TargetPrice = 632m
        });
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(binanceClient, new FakeBinanceEndpointsService()),
            orderRepository,
            new FakePositionRepository(),
            tradeExecutionRepository,
            new FakeOrderStatusService(),
            cooldownService,
            new FakeRiskManagementService(),
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            BuildConfiguration(),
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.DecisionWorker,
                CorrelationId: "corr-a"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("net profit", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, feeGuard.EvaluateCalls);
        Assert.Equal(0, binanceClient.CallCount);
    }

    [Fact]
    public async Task SpotOpenLong_FeeGuardAllowed_ContinuesToOrderPlacement_WithSingleEvaluation()
    {
        var cooldownService = new FakeTradeCooldownService(new CooldownCheckResult());
        var orderRepository = new FakeOrderRepository();
        var tradeExecutionRepository = new FakeTradeExecutionRepository();
        var binanceClient = new FakeBinanceClientService();
        var riskService = new FakeRiskManagementService
        {
            CheckOrderResult = new RiskCheckResult
            {
                IsAllowed = true,
                Reason = "ok",
                TakeProfitPrice = 645m,
                StopLossPrice = 620m
            }
        };
        var feeGuard = new FakeFeeProfitGuard(new FeeProfitGuardResult
        {
            IsAllowed = true,
            Reason = "Fee/profit guard passed."
        });
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(binanceClient, new FakeBinanceEndpointsService()),
            orderRepository,
            new FakePositionRepository(),
            tradeExecutionRepository,
            new FakeOrderStatusService(),
            cooldownService,
            riskService,
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            BuildConfiguration(),
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.Api,
                TradingMode: TradingMode.Spot,
                ExecutionIntent: TradeExecutionIntent.OpenLong,
                RawSignal: TradeSignal.Buy,
                RequiresReducedPositionSize: true,
                CorrelationId: "corr-b"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, feeGuard.EvaluateCalls);
        Assert.Equal(1, binanceClient.CallCount);
        Assert.Equal("ManualApi", feeGuard.LastRequest?.Caller);
        Assert.Equal("RiskManagementService.TakeProfitPrice", feeGuard.LastRequest?.TargetSource);
        Assert.Equal(645m, feeGuard.LastRequest?.TargetPrice);
        Assert.Equal(620m, feeGuard.LastRequest?.StopLossPrice);
        Assert.NotNull(riskService.LastCheckOrderArgs);
        Assert.Equal(TradingMode.Spot, riskService.LastCheckOrderArgs!.TradingMode);
        Assert.Equal(TradeExecutionIntent.OpenLong, riskService.LastCheckOrderArgs.ExecutionIntent);
        Assert.Equal(TradeSignal.Buy, riskService.LastCheckOrderArgs.RawSignal);
        Assert.True(riskService.LastCheckOrderArgs.RequiresReducedPositionSize);
    }

    [Fact]
    public async Task SpotOpenLong_WithStrategyExpectedTarget_PassesExpectedTargetToFeeGuard()
    {
        var cooldownService = new FakeTradeCooldownService(new CooldownCheckResult());
        var binanceClient = new FakeBinanceClientService();
        var riskService = new FakeRiskManagementService
        {
            CheckOrderResult = new RiskCheckResult
            {
                IsAllowed = true,
                Reason = "ok",
                TakeProfitPrice = 645m,
                StopLossPrice = 620m
            }
        };
        var feeGuard = new FakeFeeProfitGuard(new FeeProfitGuardResult
        {
            IsAllowed = true,
            Reason = "Fee/profit guard passed."
        });
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(binanceClient, new FakeBinanceEndpointsService()),
            new FakeOrderRepository(),
            new FakePositionRepository(),
            new FakeTradeExecutionRepository(),
            new FakeOrderStatusService(),
            cooldownService,
            riskService,
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            BuildConfiguration(),
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.DecisionWorker,
                TradingMode: TradingMode.Spot,
                ExecutionIntent: TradeExecutionIntent.OpenLong,
                RawSignal: TradeSignal.Buy,
                ExpectedTargetPrice: 633m,
                ExpectedMovePercent: 0.32m,
                ExpectedTargetSource: "MovingAverageTrendStrategy.LowVolBreakoutExpectedTarget",
                BreakoutRangeHigh: 632m,
                BreakoutRangeLow: 630m,
                BreakoutThresholdPrice: 632.1m),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, feeGuard.EvaluateCalls);
        Assert.Equal(633m, feeGuard.LastRequest?.TargetPrice);
        Assert.Equal("MovingAverageTrendStrategy.LowVolBreakoutExpectedTarget", feeGuard.LastRequest?.TargetSource);
    }

    [Fact]
    public async Task SpotOpenLong_WeakStrategyExpectedTarget_IsBlockedByFeeGuard()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:UseFeeGuard"] = "true",
                ["Trading:FeeRatePercent"] = "0.1",
                ["Trading:EstimatedSpreadPercent"] = "0.05",
                ["Trading:MinExpectedMovePercent"] = "0.3",
                ["Trading:MinNetProfitPercent"] = "0.15"
            })
            .Build();
        var feeGuard = new FeeProfitGuard(
            configuration,
            new FakePositionRepository(),
            new FakePriceCacheService(631m),
            new FakeSpotCommissionRateResolver(0.1m, "ConfigFallback"),
            new NoOpExpectedMoveBlockObservability(),
            NullLogger<FeeProfitGuard>.Instance);
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(new FakeBinanceClientService(), new FakeBinanceEndpointsService()),
            new FakeOrderRepository(),
            new FakePositionRepository(),
            new FakeTradeExecutionRepository(),
            new FakeOrderStatusService(),
            new FakeTradeCooldownService(new CooldownCheckResult()),
            new FakeRiskManagementService
            {
                CheckOrderResult = new RiskCheckResult
                {
                    IsAllowed = true,
                    Reason = "ok",
                    TakeProfitPrice = 650m,
                    StopLossPrice = 620m
                }
            },
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            BuildConfiguration(),
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.DecisionWorker,
                TradingMode: TradingMode.Spot,
                ExecutionIntent: TradeExecutionIntent.OpenLong,
                RawSignal: TradeSignal.Buy,
                ExpectedTargetPrice: 631.9m,
                ExpectedMovePercent: 0.14m,
                ExpectedTargetSource: "MovingAverageTrendStrategy.LowVolBreakoutExpectedTarget"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("minimum threshold", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotOpenLong_MissingExpectedTarget_LogsFallbackAndUsesRiskTakeProfit()
    {
        var cooldownService = new FakeTradeCooldownService(new CooldownCheckResult());
        var binanceClient = new FakeBinanceClientService();
        var riskService = new FakeRiskManagementService
        {
            CheckOrderResult = new RiskCheckResult
            {
                IsAllowed = true,
                Reason = "ok",
                TakeProfitPrice = 645m,
                StopLossPrice = 620m
            }
        };
        var feeGuard = new FakeFeeProfitGuard(new FeeProfitGuardResult
        {
            IsAllowed = true,
            Reason = "Fee/profit guard passed."
        });
        var handlerLogger = new CapturingLogger<PlaceSpotOrderCommandHandler>();
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(binanceClient, new FakeBinanceEndpointsService()),
            new FakeOrderRepository(),
            new FakePositionRepository(),
            new FakeTradeExecutionRepository(),
            new FakeOrderStatusService(),
            cooldownService,
            riskService,
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            BuildConfiguration(),
            handlerLogger);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.DecisionWorker,
                TradingMode: TradingMode.Spot,
                ExecutionIntent: TradeExecutionIntent.OpenLong,
                RawSignal: TradeSignal.Buy,
                ExpectedTargetPrice: null,
                ExpectedMovePercent: null,
                ExpectedTargetSource: null),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(645m, feeGuard.LastRequest?.TargetPrice);
        Assert.Equal("RiskManagementService.TakeProfitPrice", feeGuard.LastRequest?.TargetSource);

        var fallbackLog = handlerLogger.Entries.FirstOrDefault(x =>
            x.Message.Contains("expected target resolution", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(fallbackLog);
        Assert.False(fallbackLog!.Values["ExpectedTargetPrice"] is decimal);
        Assert.Equal(645m, Convert.ToDecimal(fallbackLog.Values["ResolvedTargetPrice"]));
        Assert.Equal("RiskManagementService.TakeProfitPrice", Convert.ToString(fallbackLog.Values["ResolvedTargetSource"]));
        Assert.True(Convert.ToBoolean(fallbackLog.Values["FallbackTargetUsed"]));
    }

    [Fact]
    public async Task SpotOpenLong_WeakNormalTrendExpectedTarget_IsBlockedByFeeGuard()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:UseFeeGuard"] = "true",
                ["Trading:RequireStrategyExpectedTargetForSpotOpenLong"] = "false",
                ["Trading:FeeRatePercent"] = "0.1",
                ["Trading:EstimatedSpreadPercent"] = "0.05",
                ["Trading:MinExpectedMovePercent"] = "0.3",
                ["Trading:MinNetProfitPercent"] = "0.15"
            })
            .Build();
        var feeGuard = new FeeProfitGuard(
            configuration,
            new FakePositionRepository(),
            new FakePriceCacheService(2060.42m),
            new FakeSpotCommissionRateResolver(0m, "BinanceSymbolCommission"),
            new NoOpExpectedMoveBlockObservability(),
            NullLogger<FeeProfitGuard>.Instance);
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(new FakeBinanceClientService(), new FakeBinanceEndpointsService()),
            new FakeOrderRepository(),
            new FakePositionRepository(),
            new FakeTradeExecutionRepository(),
            new FakeOrderStatusService(),
            new FakeTradeCooldownService(new CooldownCheckResult()),
            new FakeRiskManagementService
            {
                CheckOrderResult = new RiskCheckResult
                {
                    IsAllowed = true,
                    Reason = "ok",
                    TakeProfitPrice = 2072.78m,
                    StopLossPrice = 2052m
                }
            },
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(2060.42m),
            configuration,
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.ETHUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.DecisionWorker,
                TradingMode: TradingMode.Spot,
                ExecutionIntent: TradeExecutionIntent.OpenLong,
                RawSignal: TradeSignal.Buy,
                ExpectedTargetPrice: 2061.10m,
                ExpectedMovePercent: 0.033m,
                ExpectedTargetSource: "MovingAverageTrendStrategy.NormalTrendExpectedTarget"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("minimum threshold", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotOpenLong_TinyNormalTrendExpectedMove_RemainsBlockedByFeeGuard()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:UseFeeGuard"] = "true",
                ["Trading:RequireStrategyExpectedTargetForSpotOpenLong"] = "false",
                ["Trading:FeeRatePercent"] = "0.1",
                ["Trading:EstimatedSpreadPercent"] = "0.05",
                ["Trading:MinExpectedMovePercent"] = "0.20",
                ["Trading:MinNetProfitPercent"] = "0.08"
            })
            .Build();
        var feeGuard = new FeeProfitGuard(
            configuration,
            new FakePositionRepository(),
            new FakePriceCacheService(631m),
            new FakeSpotCommissionRateResolver(0.1m, "ConfigFallback"),
            new NoOpExpectedMoveBlockObservability(),
            NullLogger<FeeProfitGuard>.Instance);
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(new FakeBinanceClientService(), new FakeBinanceEndpointsService()),
            new FakeOrderRepository(),
            new FakePositionRepository(),
            new FakeTradeExecutionRepository(),
            new FakeOrderStatusService(),
            new FakeTradeCooldownService(new CooldownCheckResult()),
            new FakeRiskManagementService
            {
                CheckOrderResult = new RiskCheckResult
                {
                    IsAllowed = true,
                    Reason = "ok",
                    TakeProfitPrice = 633m,
                    StopLossPrice = 628m
                }
            },
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            configuration,
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.DecisionWorker,
                TradingMode: TradingMode.Spot,
                ExecutionIntent: TradeExecutionIntent.OpenLong,
                RawSignal: TradeSignal.Buy,
                ExpectedTargetPrice: 631.1512m,
                ExpectedMovePercent: 0.0239612820m,
                ExpectedTargetSource: "MovingAverageTrendStrategy.NormalTrendExpectedTarget"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("minimum threshold", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotOpenLong_MissingExpectedTarget_WithRequireStrategyTargetTrue_BlocksEntry()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:RequireStrategyExpectedTargetForSpotOpenLong"] = "true"
            })
            .Build();
        var feeGuard = new FakeFeeProfitGuard(new FeeProfitGuardResult
        {
            IsAllowed = true,
            Reason = "should not be called"
        });
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(new FakeBinanceClientService(), new FakeBinanceEndpointsService()),
            new FakeOrderRepository(),
            new FakePositionRepository(),
            new FakeTradeExecutionRepository(),
            new FakeOrderStatusService(),
            new FakeTradeCooldownService(new CooldownCheckResult()),
            new FakeRiskManagementService
            {
                CheckOrderResult = new RiskCheckResult
                {
                    IsAllowed = true,
                    Reason = "ok",
                    TakeProfitPrice = 645m,
                    StopLossPrice = 620m
                }
            },
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            configuration,
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.DecisionWorker,
                TradingMode: TradingMode.Spot,
                ExecutionIntent: TradeExecutionIntent.OpenLong,
                RawSignal: TradeSignal.Buy),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("strategy expected target is required", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, feeGuard.EvaluateCalls);
    }

    [Fact]
    public async Task SpotCloseLong_SkipsEntryFeeGuard_AndStillPlacesOrder()
    {
        var cooldownService = new FakeTradeCooldownService(new CooldownCheckResult());
        var orderRepository = new FakeOrderRepository();
        var tradeExecutionRepository = new FakeTradeExecutionRepository();
        var binanceClient = new FakeBinanceClientService();
        var feeGuard = new FakeFeeProfitGuard(new FeeProfitGuardResult
        {
            IsAllowed = false,
            Reason = "should not be called"
        });
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(binanceClient, new FakeBinanceEndpointsService()),
            orderRepository,
            new FakePositionRepository(),
            tradeExecutionRepository,
            new FakeOrderStatusService(),
            cooldownService,
            new FakeRiskManagementService(),
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            BuildConfiguration(),
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.SELL,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.TradeMonitorWorker,
                CloseReason: CloseReason.StopLoss,
                CorrelationId: "corr-c"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, feeGuard.EvaluateCalls);
        Assert.Equal(1, binanceClient.CallCount);
    }

    [Fact]
    public async Task DecisionWorkerOriginCommand_PassesRiskContextIntoHandlerCheck()
    {
        var cooldownService = new FakeTradeCooldownService(new CooldownCheckResult());
        var riskService = new FakeRiskManagementService
        {
            CheckOrderResult = new RiskCheckResult
            {
                IsAllowed = true,
                Reason = "ok",
                TakeProfitPrice = 700m,
                StopLossPrice = 600m
            }
        };
        var feeGuard = new FakeFeeProfitGuard(new FeeProfitGuardResult
        {
            IsAllowed = true,
            Reason = "Fee/profit guard passed."
        });
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(new FakeBinanceClientService(), new FakeBinanceEndpointsService()),
            new FakeOrderRepository(),
            new FakePositionRepository(),
            new FakeTradeExecutionRepository(),
            new FakeOrderStatusService(),
            cooldownService,
            riskService,
            feeGuard,
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            BuildConfiguration(),
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.DecisionWorker,
                CorrelationId: "decision-context",
                TradingMode: TradingMode.Spot,
                ExecutionIntent: TradeExecutionIntent.OpenLong,
                RawSignal: TradeSignal.Buy,
                RequiresReducedPositionSize: true),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(riskService.LastCheckOrderArgs);
        Assert.Equal(TradingMode.Spot, riskService.LastCheckOrderArgs!.TradingMode);
        Assert.Equal(TradeExecutionIntent.OpenLong, riskService.LastCheckOrderArgs.ExecutionIntent);
        Assert.Equal(TradeSignal.Buy, riskService.LastCheckOrderArgs.RawSignal);
        Assert.True(riskService.LastCheckOrderArgs.RequiresReducedPositionSize);
        Assert.Equal("DecisionWorker", feeGuard.LastRequest?.Caller);
        Assert.Equal(700m, feeGuard.LastRequest?.TargetPrice);
        Assert.Equal(600m, feeGuard.LastRequest?.StopLossPrice);
    }

    [Fact]
    public async Task ManualBuy_UsesSafeRiskContextDefaults()
    {
        var cooldownService = new FakeTradeCooldownService(new CooldownCheckResult());
        var riskService = new FakeRiskManagementService();
        var handler = new PlaceSpotOrderCommandHandler(
            new FakeToolService(new FakeBinanceClientService(), new FakeBinanceEndpointsService()),
            new FakeOrderRepository(),
            new FakePositionRepository(),
            new FakeTradeExecutionRepository(),
            new FakeOrderStatusService(),
            cooldownService,
            riskService,
            new FakeFeeProfitGuard(new FeeProfitGuardResult { IsAllowed = true, Reason = "ok" }),
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
            BuildConfiguration(),
            NullLogger<PlaceSpotOrderCommandHandler>.Instance);

        var result = await handler.Handle(
            new PlaceSpotOrderCommand(
                TradingSymbol.BNBUSDT,
                OrderSide.BUY,
                0.01m,
                Price: null,
                IsLimitOrder: false,
                OrderSource: OrderSource.Api,
                CorrelationId: "manual-default"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(riskService.LastCheckOrderArgs);
        Assert.Equal(TradingMode.Spot, riskService.LastCheckOrderArgs!.TradingMode);
        Assert.Equal(TradeExecutionIntent.OpenLong, riskService.LastCheckOrderArgs.ExecutionIntent);
        Assert.Equal(TradeSignal.Buy, riskService.LastCheckOrderArgs.RawSignal);
        Assert.False(riskService.LastCheckOrderArgs.RequiresReducedPositionSize);
    }

    [Fact]
    public async Task ConfidenceAboveThreshold_AllowsPipelineToContinue()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService,
            confidenceGate: new FakeConfidenceGate(new ConfidenceGateResult
            {
                IsAllowed = true,
                Reason = "Confidence gate passed.",
                StrategyName = "MovingAverageCrossover",
                Symbol = TradingSymbol.BNBUSDT,
                Action = TradeSignal.Buy,
                ExecutionIntent = TradeExecutionIntent.OpenLong,
                Confidence = 0.82m,
                MinConfidence = 0.70m
            }));

        Assert.Equal(1, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.Equal(0.70m, decisionRepository.LastUpdated!.MinConfidence);
        Assert.Equal(1, decisionRepository.LastUpdated.LocalOrderId);
        Assert.Equal(2, decisionRepository.LastUpdated.ExchangeOrderId);
    }

    [Fact]
    public async Task ConfidenceBelowThreshold_BlocksBeforeExecution_AndPersistsReason()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService,
            confidenceGate: new FakeConfidenceGate(new ConfidenceGateResult
            {
                IsAllowed = false,
                Reason = "Confidence below minimum threshold.",
                StrategyName = "MovingAverageCrossover",
                Symbol = TradingSymbol.BNBUSDT,
                Action = TradeSignal.Buy,
                ExecutionIntent = TradeExecutionIntent.OpenLong,
                Confidence = 0.62m,
                MinConfidence = 0.70m
            }));

        Assert.Equal(0, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.Equal(0.70m, decisionRepository.LastUpdated!.MinConfidence);
        Assert.Equal(0.62m, decisionRepository.LastUpdated.Confidence);
        Assert.Equal("Confidence below minimum threshold.", decisionRepository.LastUpdated.ExecutionError);
        Assert.Equal("Confidence below minimum threshold.", decisionRepository.LastUpdated.RiskReason);
        Assert.Null(decisionRepository.LastUpdated.LocalOrderId);
    }

    [Fact]
    public async Task SpotCloseLong_DuplicateInFlightCloseOrder_IsSkippedEndToEnd()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();
        var confidenceGate = new FakeConfidenceGate(new ConfidenceGateResult
        {
            IsAllowed = true,
            Reason = "Confidence gate passed.",
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Confidence = 0.45m,
            MinConfidence = 0.45m
        });
        var guardConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:AllowAddToPosition"] = "false",
                ["Trading:MaxOpenPositionsPerSymbol"] = "1"
            })
            .Build();
        var positionExecutionGuard = new PositionExecutionGuard(
            guardConfiguration,
            new GuardPositionRepository(new Position
            {
                Id = 64,
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.15m,
                IsOpen = true
            }),
            new GuardOrderRepository(hasInFlightCloseOrder: true),
            NullLogger<PositionExecutionGuard>.Instance);

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Sell, TradingMode.Spot, TradeExecutionIntent.CloseLong),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService,
            positionExecutionGuard: positionExecutionGuard,
            confidenceGate: confidenceGate);

        Assert.Equal(0, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.Equal(DecisionStatus.Skipped, decisionRepository.LastUpdated!.DecisionStatus);
        Assert.Equal("Execution skipped - close order already in-flight for position.", decisionRepository.LastUpdated.ExecutionError);
        Assert.Null(decisionRepository.LastUpdated.MinConfidence);
        Assert.Equal(0, confidenceGate.EvaluateCalls);
        Assert.Null(decisionRepository.LastUpdated.LocalOrderId);
        Assert.Null(decisionRepository.LastUpdated.ExchangeOrderId);
    }

    [Fact]
    public async Task SpotCloseLong_UsesAndPersistsEffectiveExitMinConfidence045()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Sell, TradingMode.Spot, TradeExecutionIntent.CloseLong),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService,
            confidenceGate: new FakeConfidenceGate(new ConfidenceGateResult
            {
                IsAllowed = true,
                Reason = "Confidence gate passed.",
                StrategyName = "MovingAverageCrossover",
                Symbol = TradingSymbol.BNBUSDT,
                Action = TradeSignal.Sell,
                ExecutionIntent = TradeExecutionIntent.CloseLong,
                Confidence = 0.45m,
                MinConfidence = 0.45m
            }));

        Assert.Equal(1, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.Equal(0.45m, decisionRepository.LastUpdated!.MinConfidence);
    }

    [Fact]
    public async Task SpotCloseLongExit_WithAllowedExitConfidence_StillHonorsCooldownGuard()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Sell, TradingMode.Spot, TradeExecutionIntent.CloseLong),
            cooldownResult: new CooldownCheckResult
            {
                IsInCooldown = true,
                RemainingSeconds = 15,
                LastTradeAtUtc = DateTime.UtcNow.AddSeconds(-5)
            },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService,
            confidenceGate: new FakeConfidenceGate(new ConfidenceGateResult
            {
                IsAllowed = true,
                Reason = "Confidence gate passed.",
                StrategyName = "MovingAverageCrossover",
                Symbol = TradingSymbol.BNBUSDT,
                Action = TradeSignal.Sell,
                ExecutionIntent = TradeExecutionIntent.CloseLong,
                Confidence = 0.50m,
                MinConfidence = 0.50m
            }));

        Assert.Equal(0, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.Equal(GuardStage.Cooldown, decisionRepository.LastUpdated!.GuardStage);
        Assert.Contains("cooldown", decisionRepository.LastUpdated.ExecutionError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SymbolRanking_SelectsBestValidSymbol_AndSkipsWeakCandidates()
    {
        var selectMethod = typeof(DecisionWorker).GetMethod("SelectRankedEntrySymbols", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(selectMethod);

        var decisions = new Dictionary<TradingSymbol, DecisionResult>
        {
            [TradingSymbol.ETHUSDT] = BuildDecisionWithScore(TradeSignal.Buy, 0.35m, 0.02m),
            [TradingSymbol.BNBUSDT] = BuildDecisionWithScore(TradeSignal.Buy, 0.78m, 0.45m),
            [TradingSymbol.SOLUSDT] = BuildDecisionWithScore(TradeSignal.Buy, 0.66m, 0.15m)
        };

        var selected = (IReadOnlyList<TradingSymbol>)selectMethod!.Invoke(null, new object[]
        {
            decisions,
            60m,
            1,
            1
        })!;

        Assert.Single(selected);
        Assert.Equal(TradingSymbol.BNBUSDT, selected[0]);
    }

    [Fact]
    public void SymbolRanking_GlobalMaxOpenPositionsReached_SelectsNoEntry()
    {
        var selectMethod = typeof(DecisionWorker).GetMethod("SelectRankedEntrySymbols", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(selectMethod);

        var decisions = new Dictionary<TradingSymbol, DecisionResult>
        {
            [TradingSymbol.BNBUSDT] = BuildDecisionWithScore(TradeSignal.Buy, 0.81m, 0.50m)
        };

        var selected = (IReadOnlyList<TradingSymbol>)selectMethod!.Invoke(null, new object[]
        {
            decisions,
            30m,
            1,
            0
        })!;

        Assert.Empty(selected);
    }

    [Fact]
    public void SymbolRanking_NoBuyCandidates_SelectsNoTrade()
    {
        var selectMethod = typeof(DecisionWorker).GetMethod("SelectRankedEntrySymbols", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(selectMethod);

        var decisions = new Dictionary<TradingSymbol, DecisionResult>
        {
            [TradingSymbol.ETHUSDT] = BuildDecisionWithScore(TradeSignal.Hold, 0.20m, 0m),
            [TradingSymbol.BNBUSDT] = BuildDecisionWithScore(TradeSignal.Sell, 0.60m, 0m, TradeExecutionIntent.CloseLong)
        };

        var selected = (IReadOnlyList<TradingSymbol>)selectMethod!.Invoke(null, new object[]
        {
            decisions,
            30m,
            1,
            1
        })!;

        Assert.Empty(selected);
    }

    [Fact]
    public async Task SymbolRanking_SelectedEntry_IsNotReDecided_AndStillRunsGuardPipeline()
    {
        var bnbDecision = BuildDecisionWithScore(TradingSymbol.BNBUSDT, TradeSignal.Buy, 0.90m, 0.60m);
        var ethDecision = BuildDecisionWithScore(TradingSymbol.ETHUSDT, TradeSignal.Buy, 0.40m, 0.05m);
        var decisionService = new RecordingDecisionService(new Dictionary<TradingSymbol, DecisionResult>
        {
            [TradingSymbol.BNBUSDT] = bnbDecision,
            [TradingSymbol.ETHUSDT] = ethDecision
        });
        var riskManagementService = new FakeRiskManagementService();
        var cooldownService = new FakeTradeCooldownService(new CooldownCheckResult { IsInCooldown = false });
        var idempotencyService = new FakeTradeIdempotencyService(isDuplicate: false);
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessRankedSymbolsAsync(
            [TradingSymbol.BNBUSDT, TradingSymbol.ETHUSDT],
            decisionService,
            riskManagementService,
            executionService,
            cooldownService,
            idempotencyService,
            decisionRepository,
            maxSymbolsToTradePerCycle: 1,
            minOpportunityScore: 10m,
            globalMaxOpenPositions: 5);

        Assert.Equal(1, decisionService.GetCallCount(TradingSymbol.BNBUSDT));
        Assert.Equal(1, decisionService.GetCallCount(TradingSymbol.ETHUSDT));
        Assert.All(decisionService.GetAllowStateMutationFlags(), flag => Assert.False(flag));
        Assert.Equal(1, executionService.ExecuteCalls);
        Assert.NotNull(riskManagementService.LastValidateTradeArgs);
        Assert.Single(cooldownService.MarkedSymbols);
        Assert.Equal(TradingSymbol.BNBUSDT, cooldownService.MarkedSymbols[0]);
        Assert.Equal(1, idempotencyService.MarkExecutedCalls);
        Assert.NotNull(decisionRepository.LastAdded);
        Assert.NotNull(decisionRepository.LastUpdated);
    }

    private static async Task InvokeProcessSymbolAsync(
        DecisionResult decisionResult,
        CooldownCheckResult cooldownResult,
        bool idempotencyAllowed,
        FakeTradeExecutionDecisionsRepository decisionRepository,
        ITradeExecutionService executionService,
        IPositionExecutionGuard? positionExecutionGuard = null,
        IConfidenceGate? confidenceGate = null,
        FakeTradeIdempotencyService? idempotencyService = null)
    {
        var worker = new DecisionWorker(
            new DummyScopeFactory(),
            BuildConfiguration(),
            NullLogger<DecisionWorker>.Instance);

        var processSymbol = typeof(DecisionWorker).GetMethod("ProcessSymbolAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var settingsType = typeof(DecisionWorker).GetNestedType("DecisionWorkerSettings", BindingFlags.NonPublic);
        Assert.NotNull(processSymbol);
        Assert.NotNull(settingsType);

        var settings = Activator.CreateInstance(settingsType!);
        Assert.NotNull(settings);
        settingsType!.GetProperty("IntervalSeconds")!.SetValue(settings, 15);
        settingsType.GetProperty("Quantity")!.SetValue(settings, 0.01m);
        settingsType.GetProperty("Symbols")!.SetValue(settings, new List<TradingSymbol> { TradingSymbol.BNBUSDT });
        settingsType.GetProperty("ExecutionEnabled")!.SetValue(settings, true);
        settingsType.GetProperty("UseMarketOrders")!.SetValue(settings, true);
        settingsType.GetProperty("MinExecutionConfidence")!.SetValue(settings, 0.25m);
        settingsType.GetProperty("TradeCooldownSeconds")!.SetValue(settings, 30);
        settingsType.GetProperty("IdempotencyWindowSeconds")!.SetValue(settings, 30);

        var tradeDecisionService = new TradeDecisionService(
            new FakeDecisionService(decisionResult),
            BuildConfiguration(),
            NullLogger<TradeDecisionService>.Instance);
        var riskManagementService = new FakeRiskManagementService();
        var cooldownService = new FakeTradeCooldownService(cooldownResult);
        var resolvedIdempotencyService = idempotencyService ?? new FakeTradeIdempotencyService(!idempotencyAllowed);
        var resolvedPositionExecutionGuard = positionExecutionGuard ?? new FakePositionExecutionGuard(new PositionExecutionGuardResult
        {
            IsAllowed = true,
            Reason = "ok",
            OpenPositionQuantity = 0m
        });
        var resolvedConfidenceGate = confidenceGate ?? new FakeConfidenceGate(new ConfidenceGateResult
        {
            IsAllowed = true,
            Reason = "Confidence gate passed.",
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = decisionResult.Action,
            ExecutionIntent = decisionResult.ExecutionIntent,
            Confidence = decisionResult.Confidence,
            MinConfidence = 0.70m
        });

        var task = (Task)processSymbol!.Invoke(worker, new object[]
        {
            TradingSymbol.BNBUSDT,
            0.01m,
            settings!,
            "test-correlation-id",
            CancellationToken.None,
            tradeDecisionService,
            riskManagementService,
            executionService,
            cooldownService,
            resolvedIdempotencyService,
            decisionRepository,
            resolvedPositionExecutionGuard,
            resolvedConfidenceGate,
            new FakeSpotPositionSizingService(),
            null
        })!;

        await task;
    }

    private static async Task InvokeProcessRankedSymbolsAsync(
        IReadOnlyList<TradingSymbol> symbols,
        IDecisionService decisionService,
        FakeRiskManagementService riskManagementService,
        FakeTradeExecutionService executionService,
        FakeTradeCooldownService cooldownService,
        FakeTradeIdempotencyService idempotencyService,
        FakeTradeExecutionDecisionsRepository decisionRepository,
        int maxSymbolsToTradePerCycle,
        decimal minOpportunityScore,
        int globalMaxOpenPositions)
    {
        var worker = new DecisionWorker(
            new DummyScopeFactory(),
            BuildConfiguration(),
            NullLogger<DecisionWorker>.Instance);

        var processRankedSymbols = typeof(DecisionWorker).GetMethod("ProcessRankedSymbolsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var settingsType = typeof(DecisionWorker).GetNestedType("DecisionWorkerSettings", BindingFlags.NonPublic);
        Assert.NotNull(processRankedSymbols);
        Assert.NotNull(settingsType);

        var settings = Activator.CreateInstance(settingsType!);
        Assert.NotNull(settings);
        settingsType!.GetProperty("IntervalSeconds")!.SetValue(settings, 15);
        settingsType.GetProperty("Quantity")!.SetValue(settings, 0.01m);
        settingsType.GetProperty("Symbols")!.SetValue(settings, symbols.ToList());
        settingsType.GetProperty("ExecutionEnabled")!.SetValue(settings, true);
        settingsType.GetProperty("UseMarketOrders")!.SetValue(settings, true);
        settingsType.GetProperty("MinExecutionConfidence")!.SetValue(settings, 0.25m);
        settingsType.GetProperty("TradeCooldownSeconds")!.SetValue(settings, 30);
        settingsType.GetProperty("IdempotencyWindowSeconds")!.SetValue(settings, 30);
        settingsType.GetProperty("EnableSymbolRanking")!.SetValue(settings, true);
        settingsType.GetProperty("MaxSymbolsToTradePerCycle")!.SetValue(settings, maxSymbolsToTradePerCycle);
        settingsType.GetProperty("MinOpportunityScore")!.SetValue(settings, minOpportunityScore);
        settingsType.GetProperty("GlobalMaxOpenPositions")!.SetValue(settings, globalMaxOpenPositions);
        settingsType.GetProperty("SymbolQuantities")!.SetValue(settings, new Dictionary<TradingSymbol, decimal>());

        var tradeDecisionService = new TradeDecisionService(
            decisionService,
            BuildConfiguration(),
            NullLogger<TradeDecisionService>.Instance);

        var confidenceGate = new FakeConfidenceGate(new ConfidenceGateResult
        {
            IsAllowed = true,
            Reason = "Confidence gate passed.",
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Confidence = 0.90m,
            MinConfidence = 0.25m
        });

        var task = (Task)processRankedSymbols!.Invoke(worker, new object[]
        {
            settings!,
            "ranked-test-correlation-id",
            CancellationToken.None,
            tradeDecisionService,
            riskManagementService,
            executionService,
            cooldownService,
            idempotencyService,
            decisionRepository,
            new FakePositionExecutionGuard(new PositionExecutionGuardResult
            {
                IsAllowed = true,
                Reason = "ok",
                OpenPositionQuantity = 0m
            }),
            confidenceGate,
            new FakePositionRepository(),
            new FakeSpotPositionSizingService()
        })!;

        await task;
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DecisionEngine:MinConfidence"] = "0.25",
                ["TradingDefaults:Symbol"] = "BNBUSDT",
                ["TradingDefaults:Quantity"] = "0.01"
            })
            .Build();
    }

    private static DecisionResult BuildDecision(
        TradeSignal action,
        TradingMode tradingMode = TradingMode.Spot,
        TradeExecutionIntent? executionIntent = null)
    {
        var resolvedIntent = executionIntent ?? (action == TradeSignal.Buy ? TradeExecutionIntent.OpenLong : TradeExecutionIntent.CloseLong);
        return new DecisionResult
        {
            Action = action,
            RawSignal = action,
            TradingMode = tradingMode,
            ExecutionIntent = resolvedIntent,
            Confidence = 0.9m,
            Reason = "test",
            Candidate = new TradeCandidate
            {
                Symbol = TradingSymbol.BNBUSDT,
                Side = action == TradeSignal.Sell ? OrderSide.SELL : OrderSide.BUY,
                Quantity = 0.01m,
                Price = 631m,
                RawSignal = action,
                TradingMode = tradingMode,
                ExecutionIntent = resolvedIntent
            }
        };
    }

    private static DecisionResult BuildDecisionWithScore(
        TradingSymbol symbol,
        TradeSignal action,
        decimal confidence,
        decimal expectedMovePercent,
        TradeExecutionIntent? executionIntent = null)
    {
        var resolvedIntent = executionIntent ?? (action == TradeSignal.Buy ? TradeExecutionIntent.OpenLong : TradeExecutionIntent.CloseLong);
        return new DecisionResult
        {
            Action = action,
            RawSignal = action,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = resolvedIntent,
            Confidence = confidence,
            TrendConfidenceScore = (int)Math.Round(confidence * 100m),
            MarketConditionScore = 70,
            Reason = "ranking-test",
            Candidate = new TradeCandidate
            {
                Symbol = symbol,
                Side = action == TradeSignal.Sell ? OrderSide.SELL : OrderSide.BUY,
                Quantity = 0.01m,
                Price = 631m,
                RawSignal = action,
                TradingMode = TradingMode.Spot,
                ExecutionIntent = resolvedIntent,
                ExpectedMovePercent = expectedMovePercent
            }
        };
    }

    private static DecisionResult BuildDecisionWithScore(
        TradeSignal action,
        decimal confidence,
        decimal expectedMovePercent,
        TradeExecutionIntent? executionIntent = null)
        => BuildDecisionWithScore(TradingSymbol.BNBUSDT, action, confidence, expectedMovePercent, executionIntent);

    private sealed class DummyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }

    private sealed class FakeDecisionService(DecisionResult result) : IDecisionService
    {
        public Task<DecisionResult> DecideAsync(
            TradingSymbol symbol,
            decimal quantity,
            CancellationToken cancellationToken = default,
            bool allowStateMutation = true)
            => Task.FromResult(result);
    }

    private sealed class RecordingDecisionService(IReadOnlyDictionary<TradingSymbol, DecisionResult> decisionsBySymbol) : IDecisionService
    {
        private readonly Dictionary<TradingSymbol, int> _callCounts = new();
        private readonly List<bool> _allowStateMutationFlags = [];

        public Task<DecisionResult> DecideAsync(
            TradingSymbol symbol,
            decimal quantity,
            CancellationToken cancellationToken = default,
            bool allowStateMutation = true)
        {
            _callCounts[symbol] = _callCounts.TryGetValue(symbol, out var count) ? count + 1 : 1;
            _allowStateMutationFlags.Add(allowStateMutation);
            return Task.FromResult(decisionsBySymbol[symbol]);
        }

        public int GetCallCount(TradingSymbol symbol)
            => _callCounts.TryGetValue(symbol, out var count) ? count : 0;

        public IReadOnlyList<bool> GetAllowStateMutationFlags() => _allowStateMutationFlags;
    }

    private sealed class FakeTradeExecutionService : ITradeExecutionService
    {
        public int ExecuteCalls { get; private set; }

        public Task<TradeExecutionResult> ExecuteMarketOrderAsync(TradeExecutionRequest request, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return Task.FromResult(new TradeExecutionResult
            {
                Success = true,
                LocalOrderId = 1,
                ExchangeOrderId = 2
            });
        }
    }

    private sealed class FakeTradeCooldownService(CooldownCheckResult checkResult) : ITradeCooldownService
    {
        public List<TradingSymbol> MarkedSymbols { get; } = [];

        public Task<CooldownCheckResult> CheckCooldownAsync(TradingSymbol symbol, int cooldownSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(checkResult);

        public Task MarkTradeExecutedAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
        {
            MarkedSymbols.Add(symbol);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingTradeExecutionService(string error) : ITradeExecutionService
    {
        public int ExecuteCalls { get; private set; }

        public Task<TradeExecutionResult> ExecuteMarketOrderAsync(TradeExecutionRequest request, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return Task.FromResult(new TradeExecutionResult
            {
                Success = false,
                Error = error
            });
        }
    }

    private sealed class FakeTradeIdempotencyService(bool isDuplicate) : ITradeIdempotencyService
    {
        private readonly HashSet<string> _executedKeys = new(StringComparer.Ordinal);

        public int MarkExecutedCalls { get; private set; }

        public string? LastMarkedKey { get; private set; }

        public Task<bool> IsDuplicateDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default)
        {
            if (isDuplicate)
                return Task.FromResult(true);

            return Task.FromResult(_executedKeys.Contains(decisionId));
        }

        public Task MarkDecisionExecutedAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default)
        {
            MarkExecutedCalls++;
            LastMarkedKey = decisionId;
            _executedKeys.Add(decisionId);
            return Task.CompletedTask;
        }

        public Task<bool> TryRegisterDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(!_executedKeys.Contains(decisionId));
    }

    private sealed class FakeTradeExecutionDecisionsRepository : ITradeExecutionDesicionsRepository
    {
        public TradeExecutionDecisions? LastAdded { get; private set; }
        public TradeExecutionDecisions? LastUpdated { get; private set; }
        private long _id = 100;

        public Task<long> AddDesicionAsync(TradeExecutionDecisions desicion)
        {
            desicion.Id = ++_id;
            LastAdded = desicion;
            return Task.FromResult(desicion.Id.Value);
        }

        public Task UpdateDesicionAsync(TradeExecutionDecisions desicion)
        {
            LastUpdated = desicion;
            return Task.CompletedTask;
        }

        public Task<TradeExecutionDecisions?> GetLatestByLocalOrderOrCorrelationAsync(
            long localOrderId,
            string? correlationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TradeExecutionDecisions?>(null);
    }

    private sealed class FakeRiskManagementService : IRiskManagementService
    {
        public RiskCheckResult CheckOrderResult { get; set; } = new() { IsAllowed = true, Reason = "ok" };
        public RiskCheckResult ValidateTradeResult { get; set; } = new() { IsAllowed = true, Reason = "ok" };
        public RiskCheckArgs? LastCheckOrderArgs { get; private set; }
        public ValidateTradeArgs? LastValidateTradeArgs { get; private set; }

        public Task<RiskCheckResult> CheckOrderAsync(
            TradingSymbol symbol,
            OrderSide side,
            decimal quantity,
            decimal? price = null,
            CancellationToken cancellationToken = default,
            bool requiresReducedPositionSize = false,
            TradingMode tradingMode = TradingMode.Spot,
            TradeSignal rawSignal = TradeSignal.Hold,
            TradeExecutionIntent executionIntent = TradeExecutionIntent.None)
        {
            LastCheckOrderArgs = new RiskCheckArgs(
                symbol,
                side,
                quantity,
                price,
                requiresReducedPositionSize,
                tradingMode,
                rawSignal,
                executionIntent);
            return Task.FromResult(CheckOrderResult);
        }

        public Task<RiskCheckResult> ValidateTrade(
            TradingSymbol symbol,
            decimal quantity,
            decimal price,
            OrderSide side,
            CancellationToken cancellationToken = default,
            bool requiresReducedPositionSize = false,
            TradingMode tradingMode = TradingMode.Spot,
            TradeSignal rawSignal = TradeSignal.Hold,
            TradeExecutionIntent executionIntent = TradeExecutionIntent.None)
        {
            LastValidateTradeArgs = new ValidateTradeArgs(
                symbol,
                quantity,
                price,
                side,
                requiresReducedPositionSize,
                tradingMode,
                rawSignal,
                executionIntent);
            return Task.FromResult(ValidateTradeResult);
        }

        public sealed record RiskCheckArgs(
            TradingSymbol Symbol,
            OrderSide Side,
            decimal Quantity,
            decimal? Price,
            bool RequiresReducedPositionSize,
            TradingMode TradingMode,
            TradeSignal RawSignal,
            TradeExecutionIntent ExecutionIntent);

        public sealed record ValidateTradeArgs(
            TradingSymbol Symbol,
            decimal Quantity,
            decimal Price,
            OrderSide Side,
            bool RequiresReducedPositionSize,
            TradingMode TradingMode,
            TradeSignal RawSignal,
            TradeExecutionIntent ExecutionIntent);
    }

    private sealed class FakeToolService(IBinanceClientService clientService, IBinanceEndpointsService endpointsService) : IToolService
    {
        public IBinanceClientService BinanceClientService { get; } = clientService;
        public IBinanceEndpointsService BinanceEndpointsService { get; } = endpointsService;
        public IBinanceSettingsService BinanceSettingsService => throw new NotSupportedException();
        public IRedisCacheService RedisCacheService => throw new NotSupportedException();
        public IOrderValidator OrderValidator => throw new NotSupportedException();
        public ISlicerService SlicerService => throw new NotSupportedException();
        public IAICLinetService AICLinetService => throw new NotSupportedException();
    }

    private sealed class FakeBinanceClientService : IBinanceClientService
    {
        public int CallCount { get; private set; }

        public Task<TResponse> Call<TResponse, TRequest>(TRequest? request, Endpoint endpoint, bool enableSignature)
        {
            CallCount++;
            object response = new TradingBot.Domain.Models.TradingEndpoints.OrderResponse
            {
                Symbol = "BNBUSDT",
                OrderId = 123456,
                Price = "631",
                ExecutedQty = "0.01",
                CummulativeQuoteQty = "6.31",
                Status = "FILLED",
                Side = "BUY",
                Type = "MARKET",
                TransactTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Fills = []
            };
            return Task.FromResult((TResponse)response);
        }
    }

    private sealed class FakeBinanceEndpointsService : IBinanceEndpointsService
    {
        private static readonly Endpoint Endpoint = new() { API = "/api/v3/order", Type = "POST" };

        public Endpoint GetEndpoint(Account account) => Endpoint;
        public Endpoint GetEndpoint(GeneralApis general) => Endpoint;
        public Endpoint GetEndpoint(MarketData marketData) => Endpoint;
        public Endpoint GetEndpoint(EndpointTrading trading) => Endpoint;
    }

    private sealed class FakeSpotPositionSizingService : ISpotPositionSizingService
    {
        public Task<SpotPositionSizingResult> ResolveOpenLongQuantityAsync(
            SpotPositionSizingRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.SymbolQuantities.TryGetValue(request.Symbol, out var symbolQuantity) && symbolQuantity > 0m)
            {
                return Task.FromResult(new SpotPositionSizingResult
                {
                    IsSuccess = true,
                    Quantity = symbolQuantity,
                    QuantitySource = SpotQuantitySource.SymbolOverride
                });
            }

            return Task.FromResult(new SpotPositionSizingResult
            {
                IsSuccess = true,
                Quantity = request.GlobalQuantity,
                QuantitySource = SpotQuantitySource.GlobalFallback
            });
        }

        public Task<SpotMinNotionalValidationResult> ValidateMinNotionalAsync(
            TradingSymbol symbol,
            decimal quantity,
            decimal price,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SpotMinNotionalValidationResult
            {
                IsValid = true,
                Quantity = quantity,
                Price = price,
                Notional = quantity * price
            });
        }
    }

    private sealed class FakeBinanceOrderNormalizationService : IBinanceOrderNormalizationService
    {
        public Task<BinanceSymbolFilters> GetSymbolFiltersAsync(string symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(new BinanceSymbolFilters { Symbol = symbol });

        public Task<BinanceOrderNormalizationResult> NormalizeNewOrderAsync(NewOrderRequest request, decimal? marketPrice, CancellationToken cancellationToken = default)
            => Task.FromResult(new BinanceOrderNormalizationResult
            {
                Filters = new BinanceSymbolFilters { Symbol = request.Symbol },
                Request = request,
                OriginalQuantity = request.Quantity,
                NormalizedQuantity = request.Quantity,
                EffectivePrice = marketPrice
            });
    }

    private sealed class FakeTimeSyncService : ITimeSyncService
    {
        public Task<long> GetAdjustedTimestampAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        public Task<long> RefreshOffsetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private sealed class FakePriceCacheService(decimal price) : IPriceCacheService
    {
        public Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<decimal?>(price);

        public Task<PriceSnapshot?> GetCachedPriceSnapshotAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<PriceSnapshot?>(new PriceSnapshot
            {
                Price = price,
                AsOfUtc = DateTime.UtcNow,
                Source = "RedisTicker"
            });

        public Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeOrderStatusService : IOrderStatusService
    {
        public Task<bool> TryUpdateProcessingStatusAsync(long orderId, ProcessingStatus expectedStatus, ProcessingStatus newStatus, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
            => Task.FromResult(true);

        public Task<bool> TrySetTradesSyncFailedAsync(long orderId, ProcessingStatus expectedStatus, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
            => Task.FromResult(true);

        public Task<bool> TrySetPositionUpdateFailedAsync(long orderId, ProcessingStatus expectedStatus, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
            => Task.FromResult(true);
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        private long _id = 0;

        public Task<long> InsertAsync(Order order, CancellationToken cancellationToken = default)
        {
            order.Id = ++_id;
            return Task.FromResult(order.Id);
        }

        public Task UpdateAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<Order?> GetByExchangeOrderIdAsync(long exchangeOrderId, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<IReadOnlyList<Order>> GetOpenOrdersAsync(TradingSymbol? symbol = null, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetFilledOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusAsync(ProcessingStatus processingStatus, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<int> GetInFlightOpeningOrderCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> HasInFlightClosingOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasActiveCloseOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<Order>> GetOpenOrdersForWorkerAsync(IDbTransaction transaction, TradingSymbol? symbol, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusForWorkerAsync(IDbTransaction transaction, ProcessingStatus processingStatus, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
    }

    private sealed class FakePositionExecutionGuard(PositionExecutionGuardResult result) : IPositionExecutionGuard
    {
        public Task<PositionExecutionGuardResult> EvaluateAsync(PositionExecutionGuardRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class NoOpExpectedMoveBlockObservability : IFeeProfitGuardExpectedMoveBlockObservability
    {
        public void RecordExpectedMoveBlock(FeeProfitGuardExpectedMoveBlockObservation observation)
        {
        }

        public void FlushAndLog(decimal currentMinExpectedMovePercent, decimal currentMinNetProfitPercent, TimeSpan reportingWindow)
        {
        }
    }

    private sealed class FakeFeeProfitGuard(FeeProfitGuardResult result) : IFeeProfitGuard
    {
        public int EvaluateCalls { get; private set; }
        public FeeProfitGuardRequest? LastRequest { get; private set; }

        public Task<FeeProfitGuardResult> EvaluateAsync(FeeProfitGuardRequest request, CancellationToken cancellationToken = default)
        {
            EvaluateCalls++;
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeSpotCommissionRateResolver(decimal feeRatePercent, string source) : ISpotCommissionRateResolver
    {
        public Task<SpotCommissionRateResolution> ResolveFeeRatePercentAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(new SpotCommissionRateResolution
            {
                FeeRatePercent = feeRatePercent,
                FeeRateSource = source
            });
    }

    private sealed class FakeConfidenceGate(ConfidenceGateResult result) : IConfidenceGate
    {
        public int EvaluateCalls { get; private set; }

        public Task<ConfidenceGateResult> EvaluateAsync(ConfidenceGateRequest request, CancellationToken cancellationToken = default)
        {
            EvaluateCalls++;
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (state is IEnumerable<KeyValuePair<string, object?>> structured)
            {
                foreach (var kv in structured)
                    values[kv.Key] = kv.Value;
            }

            Entries.Add(new LogEntry(message, values));
        }
    }

    private sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> Values);

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose()
        {
        }
    }

    private sealed class FakeTradeExecutionRepository : ITradeExecutionRepository
    {
        public Task<long> InsertAsync(TradeExecution execution, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task<TradeExecution?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<TradeExecution?>(null);
        public Task<TradeExecution?> GetByExchangeTradeIdAsync(long exchangeTradeId, CancellationToken cancellationToken = default) => Task.FromResult<TradeExecution?>(null);
        public Task<IReadOnlyList<TradeExecution>> GetByOrderIdAsync(long orderId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TradeExecution>>([]);
        public Task<IReadOnlyList<TradeExecution>> GetBySymbolAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TradeExecution>>([]);
        public Task MarkPositionProcessedByOrderAsync(long orderId, DateTime processedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakePositionRepository : IPositionRepository
    {
        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default) => Task.FromResult(position.Id == 0 ? 1L : position.Id);
        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<Position?>(null);
        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult<Position?>(null);
        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>([]);
        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>([]);
        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class GuardPositionRepository(Position? openPosition) : IPositionRepository
    {
        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default) => Task.FromResult(position.Id == 0 ? 1L : position.Id);
        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>(openPosition is null ? [] : [openPosition]);
        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>([]);
        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class GuardOrderRepository(bool hasInFlightCloseOrder) : IOrderRepository
    {
        public Task<long> InsertAsync(Order order, CancellationToken cancellationToken = default) => Task.FromResult(order.Id);
        public Task UpdateAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<Order?> GetByExchangeOrderIdAsync(long exchangeOrderId, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<IReadOnlyList<Order>> GetOpenOrdersAsync(TradingSymbol? symbol = null, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetFilledOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusAsync(ProcessingStatus processingStatus, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<int> GetInFlightOpeningOrderCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> HasInFlightClosingOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default) => Task.FromResult(hasInFlightCloseOrder);
        public Task<bool> HasActiveCloseOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default) => Task.FromResult(hasInFlightCloseOrder);
        public Task<IReadOnlyList<Order>> GetOpenOrdersForWorkerAsync(IDbTransaction transaction, TradingSymbol? symbol, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusForWorkerAsync(IDbTransaction transaction, ProcessingStatus processingStatus, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
    }
}
