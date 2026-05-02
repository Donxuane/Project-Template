using System.Data;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            new FakeBinanceOrderNormalizationService(),
            new FakeTimeSyncService(),
            new FakePriceCacheService(631m),
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
    public async Task FeeGuardBlock_PreventsDecisionExecutionBeforeTradeExecutionService()
    {
        var decisionRepository = new FakeTradeExecutionDecisionsRepository();
        var executionService = new FakeTradeExecutionService();

        await InvokeProcessSymbolAsync(
            decisionResult: BuildDecision(TradeSignal.Buy),
            cooldownResult: new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 },
            idempotencyAllowed: true,
            decisionRepository: decisionRepository,
            executionService: executionService,
            feeProfitGuard: new FakeFeeProfitGuard(new FeeProfitGuardResult
            {
                IsAllowed = false,
                Reason = "Skipped because expected net profit after fees/spread is below minimum threshold.",
                EntryPrice = 631m,
                TargetPrice = 632m,
                GrossExpectedProfitPercent = 0.16m,
                EstimatedEntryFeePercent = 0.1m,
                EstimatedExitFeePercent = 0.1m,
                EstimatedSpreadPercent = 0.05m,
                EstimatedTotalCostPercent = 0.25m,
                NetExpectedProfitPercent = -0.09m
            }));

        Assert.Equal(0, executionService.ExecuteCalls);
        Assert.NotNull(decisionRepository.LastUpdated);
        Assert.Contains("fees/spread", decisionRepository.LastUpdated!.ExecutionError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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

    private static async Task InvokeProcessSymbolAsync(
        DecisionResult decisionResult,
        CooldownCheckResult cooldownResult,
        bool idempotencyAllowed,
        FakeTradeExecutionDecisionsRepository decisionRepository,
        FakeTradeExecutionService executionService,
        IPositionExecutionGuard? positionExecutionGuard = null,
        IFeeProfitGuard? feeProfitGuard = null,
        IConfidenceGate? confidenceGate = null)
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
        var idempotencyService = new FakeTradeIdempotencyService(idempotencyAllowed);
        var resolvedPositionExecutionGuard = positionExecutionGuard ?? new FakePositionExecutionGuard(new PositionExecutionGuardResult
        {
            IsAllowed = true,
            Reason = "ok",
            OpenPositionQuantity = 0m
        });
        var resolvedFeeProfitGuard = feeProfitGuard ?? new FakeFeeProfitGuard(new FeeProfitGuardResult
        {
            IsAllowed = true,
            Reason = "ok",
            EntryPrice = 631m,
            TargetPrice = 635m,
            GrossExpectedProfitPercent = 0.63m,
            EstimatedEntryFeePercent = 0.1m,
            EstimatedExitFeePercent = 0.1m,
            EstimatedSpreadPercent = 0.05m,
            EstimatedTotalCostPercent = 0.25m,
            NetExpectedProfitPercent = 0.38m
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
            settings!,
            "test-correlation-id",
            CancellationToken.None,
            tradeDecisionService,
            riskManagementService,
            executionService,
            cooldownService,
            idempotencyService,
            decisionRepository,
            resolvedPositionExecutionGuard,
            resolvedFeeProfitGuard,
            resolvedConfidenceGate
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

    private sealed class DummyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }

    private sealed class FakeDecisionService(DecisionResult result) : IDecisionService
    {
        public Task<DecisionResult> DecideAsync(TradingSymbol symbol, decimal quantity, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
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

    private sealed class FakeTradeIdempotencyService(bool allowed) : ITradeIdempotencyService
    {
        public Task<bool> TryRegisterDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(allowed);
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
    }

    private sealed class FakeRiskManagementService : IRiskManagementService
    {
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
            => Task.FromResult(new RiskCheckResult { IsAllowed = true, Reason = "ok" });

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
            => Task.FromResult(new RiskCheckResult { IsAllowed = true, Reason = "ok" });
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
        public Task<TResponse> Call<TResponse, TRequest>(TRequest? request, Endpoint endpoint, bool enableSignature)
        {
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
        public Task<IReadOnlyList<Order>> GetOpenOrdersForWorkerAsync(IDbTransaction transaction, TradingSymbol? symbol, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusForWorkerAsync(IDbTransaction transaction, ProcessingStatus processingStatus, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
    }

    private sealed class FakePositionExecutionGuard(PositionExecutionGuardResult result) : IPositionExecutionGuard
    {
        public Task<PositionExecutionGuardResult> EvaluateAsync(PositionExecutionGuardRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class FakeFeeProfitGuard(FeeProfitGuardResult result) : IFeeProfitGuard
    {
        public Task<FeeProfitGuardResult> EvaluateAsync(FeeProfitGuardRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class FakeConfidenceGate(ConfidenceGateResult result) : IConfidenceGate
    {
        public Task<ConfidenceGateResult> EvaluateAsync(ConfidenceGateRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
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
    }
}
