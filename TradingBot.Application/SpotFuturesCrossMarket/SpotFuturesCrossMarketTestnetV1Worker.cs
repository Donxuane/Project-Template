using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models.Analytics;
using TradingBot.Domain.Models.Decision;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.SpotFuturesCrossMarket;

/// <summary>
/// Runtime worker for the SpotFuturesCrossMarketTestnetV1 strategy. Places real Binance
/// USD-M Futures TESTNET orders (fake funds) — never real-money orders.
///
/// Cycle model:
///   - Every <c>IntervalSeconds</c> the worker checks protective exits (stop loss / take
///     profit / max hold) on the open position against the live mark price.
///   - Entry, flip and signal-exit decisions are made exactly once per fully closed candle,
///     from a synchronized Spot + Futures snapshot: OpenLong / OpenShort / CloseLong /
///     CloseShort / NoTrade.
///
/// Every evaluation (including NoTrade) is persisted to the
/// spot_futures_cross_market_evaluations table and cached in Redis; every intent goes to
/// trade_execution_decisions; orders/fills/positions reuse the shared tables tagged with the
/// strategy's execution environment so the live Spot pipeline and the ETH15 worker ignore them.
/// </summary>
public sealed class SpotFuturesCrossMarketTestnetV1Worker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<SpotFuturesCrossMarketTestnetV1Worker> logger) : BackgroundService
{
    private const string Env = SpotFuturesCrossMarketSettings.ExecutionEnvironment;
    private const string StrategyName = SpotFuturesCrossMarketSettings.StrategyName;

    private readonly Dictionary<TradingSymbol, FuturesTestnetSymbolFilters> _symbolFilters = new();
    private readonly Dictionary<TradingSymbol, DateTime> _lastProcessedCandleOpenUtc = new();
    private readonly HashSet<TradingSymbol> _lastProcessedLoaded = new();

    private static string LastCandleRedisKey(SpotFuturesCrossMarketSettings s)
        => $"SpotFuturesXMarket:{s.Symbol}:{s.Interval}:LastProcessedCandleOpenMs";

    private static string LastEvaluationRedisKey(SpotFuturesCrossMarketSettings s)
        => $"SpotFuturesXMarket:{s.Symbol}:{s.Interval}:LastEvaluation";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SpotFuturesCrossMarketSettings settings;
        try
        {
            settings = SpotFuturesCrossMarketSettings.Load(configuration, hostEnvironment.ContentRootPath);
            settings.ValidateTestnetSafety(configuration);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogCritical(ex, "SpotFuturesCrossMarketTestnetV1 forbidden/unsafe configuration detected. Worker will not run.");
            throw;
        }

        if (!settings.Enabled)
        {
            logger.LogInformation("SpotFuturesCrossMarketTestnetV1 disabled by config. Worker not running.");
            return;
        }

        logger.LogInformation(
            "SpotFuturesCrossMarketTestnetV1 worker started. Symbols={Symbols} Interval={Interval} AllowTestnetOrders={AllowTestnetOrders} BalanceSizing={BalanceSizing} AllocationPercent={AllocationPercent} FallbackNotional={Notional} Leverage={Leverage} MaxOpen={MaxOpen} DailyMax={DailyMax} MaxConsecLosses={MaxConsec} CycleSeconds={CycleSeconds}",
            string.Join(",", settings.Symbols), settings.Interval, settings.AllowTestnetOrders,
            settings.UseBalanceBasedSizing, settings.BalanceAllocationPercent, settings.NotionalUsdt,
            settings.Leverage, settings.MaxOpenPositions, settings.DailyMaxTrades, settings.MaxConsecutiveLosses, settings.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(settings, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("SpotFuturesCrossMarket", StringComparison.Ordinal))
            {
                logger.LogCritical(ex, "SpotFuturesCrossMarketTestnetV1 unsafe configuration detected at runtime. Stopping worker.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SpotFuturesCrossMarketTestnetV1 cycle failed at {Time}", DateTime.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        logger.LogInformation("SpotFuturesCrossMarketTestnetV1 worker stopped.");
    }

    private async Task RunCycleAsync(SpotFuturesCrossMarketSettings settings, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var positionRepo = sp.GetRequiredService<IPositionRepository>();
        var reportWriter = sp.GetRequiredService<SpotFuturesCrossMarketReportWriter>();

        var openPositions = await positionRepo.GetOpenPositionsByEnvironmentAsync(Env, ct);
        CrossMarketDecision? lastDecision = null;

        foreach (var symbol in settings.Symbols)
        {
            ct.ThrowIfCancellationRequested();
            var symbolSettings = settings.ForSymbol(symbol);
            var openPosition = openPositions.FirstOrDefault(p => p.Symbol == symbol && p.IsOpen);

            // 1) Intra-candle protective exits against the live mark price.
            if (openPosition is not null)
            {
                openPosition = await TryProtectiveExitAsync(symbolSettings, sp, openPosition, ct);
            }

            // 2) Closed-candle strategy evaluation (entries, flips, signal exits).
            var candleReady = await TryGetNewClosedCandleAsync(symbolSettings, sp, ct);
            if (candleReady)
            {
                lastDecision = await EvaluateClosedCandleAsync(symbolSettings, sp, openPosition, ct) ?? lastDecision;
            }
        }

        // 3) Reporting from the environment-scoped rows (all symbols together).
        var closed = await positionRepo.GetClosedPositionsByEnvironmentAsync(Env, ct);
        var openNow = await positionRepo.GetOpenPositionsByEnvironmentAsync(Env, ct);
        await reportWriter.WriteAsync(settings.ReportOutputDirectory, closed, openNow, lastDecision, ct);
    }

    // --------------------------------------------------------------------- closed candle gate

    /// <summary>
    /// True when a candle newer than the last processed one has fully closed. Restart-safe:
    /// falls back to Redis, then the evaluations table, before in-memory state.
    /// </summary>
    private async Task<bool> TryGetNewClosedCandleAsync(
        SpotFuturesCrossMarketSettings settings,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var interval = settings.IntervalTimeSpan;
        var nowUtc = DateTime.UtcNow;

        // Open time of the most recent candle whose close time has passed.
        var currentOpenMs = (long)(Math.Floor((nowUtc - DateTime.UnixEpoch) / interval) - 1) * (long)interval.TotalMilliseconds;
        var latestClosedOpenUtc = DateTime.UnixEpoch.AddMilliseconds(currentOpenMs);

        if (!_lastProcessedLoaded.Contains(settings.Symbol))
        {
            _lastProcessedLoaded.Add(settings.Symbol);

            var redis = sp.GetRequiredService<IRedisCacheService>();
            var cachedMs = await redis.GetCacheValue<long?>(LastCandleRedisKey(settings));
            if (cachedMs is > 0)
            {
                _lastProcessedCandleOpenUtc[settings.Symbol] = DateTime.UnixEpoch.AddMilliseconds(cachedMs.Value);
            }
            else
            {
                var evaluationRepo = sp.GetRequiredService<ISpotFuturesCrossMarketEvaluationRepository>();
                var fromDb = await evaluationRepo.GetLastEvaluatedCandleOpenTimeAsync(settings.Symbol, settings.Interval, ct);
                if (fromDb is not null)
                    _lastProcessedCandleOpenUtc[settings.Symbol] = fromDb.Value;
            }
        }

        return !_lastProcessedCandleOpenUtc.TryGetValue(settings.Symbol, out var last) || latestClosedOpenUtc > last;
    }

    private async Task MarkCandleProcessedAsync(
        SpotFuturesCrossMarketSettings settings,
        IServiceProvider sp,
        DateTime candleOpenUtc)
    {
        _lastProcessedCandleOpenUtc[settings.Symbol] = candleOpenUtc;
        var redis = sp.GetRequiredService<IRedisCacheService>();
        await redis.SetCacheValue(LastCandleRedisKey(settings), (long)(candleOpenUtc - DateTime.UnixEpoch).TotalMilliseconds);
    }

    // --------------------------------------------------------------------- evaluation

    private async Task<CrossMarketDecision?> EvaluateClosedCandleAsync(
        SpotFuturesCrossMarketSettings settings,
        IServiceProvider sp,
        Position? openPosition,
        CancellationToken ct)
    {
        var dataService = sp.GetRequiredService<SpotFuturesCrossMarketDataService>();
        var signalEngine = sp.GetRequiredService<SpotFuturesCrossMarketSignalEngine>();

        var snapshot = await dataService.GetSnapshotAsync(settings, ct);

        // Only accept a snapshot for a candle we have not evaluated yet; a stale anchor means
        // one feed has not published the new candle, so wait for the next cycle.
        if (snapshot.MarketsInSync &&
            _lastProcessedCandleOpenUtc.TryGetValue(settings.Symbol, out var lastProcessed) &&
            snapshot.CandleOpenTimeUtc <= lastProcessed)
        {
            logger.LogInformation(
                "SpotFuturesCrossMarket waiting for fresh candle. Symbol={Symbol} AnchorOpen={AnchorOpen} LastProcessed={LastProcessed}",
                settings.Symbol, snapshot.CandleOpenTimeUtc, lastProcessed);
            return null;
        }

        var decision = signalEngine.Evaluate(settings, snapshot, openPosition?.Side);

        logger.LogInformation(
            "SpotFuturesCrossMarket EVALUATION. Symbol={Symbol} Candle={CandleOpen:o} Action={Action} SpotTrend={SpotTrend}({SpotScore}) FuturesTrend={FutTrend}({FutScore}) Momentum={Momentum:F3}% Basis={Basis:F3}% Funding={Funding} Atr={Atr:F3}% Reason={Reason}",
            settings.Symbol, snapshot.CandleOpenTimeUtc, decision.Action,
            decision.SpotTrendState, decision.SpotTrendConfidenceScore,
            decision.FuturesTrendState, decision.FuturesTrendConfidenceScore,
            decision.SpotMomentumPercent, snapshot.BasisPercent, snapshot.FundingRate,
            decision.FuturesAtrPercent, decision.Reason);

        var correlationId = NewId();
        long? positionId = openPosition?.Id;
        long? localOrderId = null;
        var executed = false;

        switch (decision.Action)
        {
            case CrossMarketAction.OpenLong or CrossMarketAction.OpenShort:
            {
                var result = await TryEnterAsync(settings, sp, decision, snapshot, correlationId, ct);
                executed = result.Executed;
                positionId = result.PositionId ?? positionId;
                localOrderId = result.LocalOrderId;
                break;
            }
            case CrossMarketAction.CloseLong or CrossMarketAction.CloseShort when openPosition is not null:
            {
                var result = await TryCloseBySignalAsync(settings, sp, openPosition, decision, correlationId, ct);
                executed = result.Executed;
                localOrderId = result.LocalOrderId;
                break;
            }
            default:
            {
                // Out-of-sync snapshots are retried on the next cycle without an audit row,
                // otherwise every retry cycle would spam duplicate Skipped decisions.
                if (snapshot.MarketsInSync)
                {
                    await PersistDecisionAsync(sp, settings, decision, correlationId, DecisionStatus.Skipped, GuardStage.None,
                        executionSuccess: false, localOrderId: null, exchangeOrderId: null, error: null);
                }

                break;
            }
        }

        if (snapshot.MarketsInSync)
        {
            await PersistEvaluationAsync(sp, settings, snapshot, decision, correlationId, executed, positionId, localOrderId, ct);
            await MarkCandleProcessedAsync(settings, sp, snapshot.CandleOpenTimeUtc);
        }

        return decision;
    }

    // --------------------------------------------------------------------- entries

    private async Task<(bool Executed, long? PositionId, long? LocalOrderId)> TryEnterAsync(
        SpotFuturesCrossMarketSettings settings,
        IServiceProvider sp,
        CrossMarketDecision decision,
        CrossMarketSnapshot snapshot,
        string correlationId,
        CancellationToken ct)
    {
        var positionRepo = sp.GetRequiredService<IPositionRepository>();
        var futuresClient = sp.GetRequiredService<IFuturesTestnetClient>();

        // Runtime guards (independent of the signal).
        var closed = await positionRepo.GetClosedPositionsByEnvironmentAsync(Env, ct);
        var openNow = await positionRepo.GetOpenPositionsByEnvironmentAsync(Env, ct);
        var openCount = openNow.Count;
        var consecutiveLosses = TrailingConsecutiveLosses(closed);
        var tradesToday = CountTradesToday(closed) + openCount;

        string? guardBlock = null;
        var guardStage = GuardStage.None;

        if (!settings.CanPlaceTestnetOrders)
        {
            guardBlock = "TestnetOrdersNotAllowed(Enabled/AllowTestnetOrders must both be true)";
            guardStage = GuardStage.Execution;
        }
        else if (openNow.Any(p => p.Symbol == settings.Symbol))
        {
            // One-way position mode: a second entry on the same symbol would net against
            // the existing exchange position and corrupt the local book.
            guardBlock = $"SymbolPositionAlreadyOpen({settings.Symbol})";
            guardStage = GuardStage.PositionGuard;
        }
        else if (openCount >= settings.MaxOpenPositions)
        {
            guardBlock = $"MaxOpenPositionsReached({openCount}/{settings.MaxOpenPositions})";
            guardStage = GuardStage.PositionGuard;
        }
        else if (consecutiveLosses >= settings.MaxConsecutiveLosses)
        {
            guardBlock = $"HardStopConsecutiveLosses({consecutiveLosses}/{settings.MaxConsecutiveLosses})";
            guardStage = GuardStage.Risk;
        }
        else if (tradesToday >= settings.DailyMaxTrades)
        {
            guardBlock = $"DailyMaxTradesReached({tradesToday}/{settings.DailyMaxTrades})";
            guardStage = GuardStage.Risk;
        }
        else if (IsInReentryCooldown(settings, closed, snapshot.CandleOpenTimeUtc))
        {
            guardBlock = $"ReentryCooldown({settings.ReentryCooldownCandles} candles after last close)";
            guardStage = GuardStage.Cooldown;
        }

        if (guardBlock is not null)
        {
            logger.LogInformation("SpotFuturesCrossMarket entry blocked. Action={Action} Reason={Reason}", decision.Action, guardBlock);
            await PersistDecisionAsync(sp, settings, decision, correlationId, DecisionStatus.Skipped, guardStage,
                executionSuccess: false, localOrderId: null, exchangeOrderId: null, error: guardBlock);
            return (false, null, null);
        }

        var side = decision.Action == CrossMarketAction.OpenLong ? OrderSide.BUY : OrderSide.SELL;

        var markPrice = snapshot.MarkPrice is > 0m
            ? snapshot.MarkPrice.Value
            : await futuresClient.GetMarkPriceAsync(settings.Symbol.ToString(), ct);
        if (markPrice <= 0m)
        {
            await PersistDecisionAsync(sp, settings, decision, correlationId, DecisionStatus.Failed, GuardStage.Execution,
                executionSuccess: false, localOrderId: null, exchangeOrderId: null, error: "MarkPriceUnavailable");
            return (false, null, null);
        }

        var quantity = await ResolveQuantityAsync(settings, sp, futuresClient, markPrice, ct);
        if (quantity <= 0m)
        {
            await PersistDecisionAsync(sp, settings, decision, correlationId, DecisionStatus.Failed, GuardStage.Execution,
                executionSuccess: false, localOrderId: null, exchangeOrderId: null,
                error: $"QuantityBelowExchangeMinimum(notional={settings.NotionalUsdt}, price={markPrice})");
            return (false, null, null);
        }

        var decisionId = await PersistDecisionAsync(sp, settings, decision, correlationId, DecisionStatus.Pending, GuardStage.None,
            executionSuccess: false, localOrderId: null, exchangeOrderId: null, error: null);

        logger.LogInformation(
            "SpotFuturesCrossMarket ORDER INTENT (entry). Symbol={Symbol} Side={Side} Quantity={Quantity} MarkPrice={MarkPrice} Notional={Notional:F2} Leverage={Leverage} StopLoss={StopLoss} TakeProfit={TakeProfit} CorrelationId={CorrelationId}",
            settings.Symbol, side, quantity, markPrice, quantity * markPrice, settings.Leverage,
            decision.StopLossPrice, decision.TakeProfitPrice, correlationId);

        try
        {
            var accounting = sp.GetRequiredService<SpotFuturesCrossMarketAccounting>();
            var orderRepo = sp.GetRequiredService<IOrderRepository>();
            var decisionRepo = sp.GetRequiredService<ITradeExecutionDesicionsRepository>();

            await futuresClient.EnsureLeverageAsync(settings.Symbol.ToString(), settings.Leverage, ct);
            var result = await futuresClient.PlaceMarketOrderAsync(settings.Symbol.ToString(), side, quantity, reduceOnly: false, ct);

            var (fills, avgPrice, filledQty, entryFee) = await ResolveFillAsync(futuresClient, settings.Symbol.ToString(), result, ct);
            var entryPrice = avgPrice > 0m ? avgPrice : markPrice;
            var executedQty = filledQty > 0m ? filledQty : quantity;

            var order = new Order
            {
                ExchangeOrderId = result.OrderId,
                CorrelationId = correlationId,
                OrderSource = OrderSource.SpotFuturesCrossMarketTestnetV1,
                CloseReason = CloseReason.None,
                Symbol = settings.Symbol,
                Side = side,
                Status = result.Status.ToOrderStatus(),
                ProcessingStatus = ProcessingStatus.PositionUpdated,
                Price = entryPrice,
                Quantity = executedQty,
                ExecutionEnvironment = Env
            };
            await orderRepo.InsertAsync(order, ct);
            await PersistExecutionsAsync(sp, order, fills, result, settings.Symbol, side, entryPrice, executedQty, ct);

            var position = await accounting.OpenAsync(
                settings.Symbol, side, executedQty, entryPrice, entryFee,
                decision.StopLossPrice, decision.TakeProfitPrice, DateTime.UtcNow, ct);

            order.ParentPositionId = position.Id;
            await orderRepo.UpdateAsync(order, ct);

            await decisionRepo.UpdateDesicionAsync(new TradeExecutionDecisions
            {
                Id = decisionId,
                CorrelationId = correlationId,
                DecisionStatus = DecisionStatus.Executed,
                GuardStage = GuardStage.Execution,
                ExecutionSuccess = true,
                LocalOrderId = order.Id,
                ExchangeOrderId = order.ExchangeOrderId,
                StopLossPrice = decision.StopLossPrice,
                TakeProfitPrice = decision.TakeProfitPrice
            });

            logger.LogInformation(
                "SpotFuturesCrossMarket ENTRY FILLED. PositionId={PositionId} Side={Side} LocalOrderId={LocalOrderId} ExchangeOrderId={ExchangeOrderId} EntryPrice={EntryPrice} Quantity={Quantity} EntryFee={EntryFee}",
                position.Id, side, order.Id, order.ExchangeOrderId, entryPrice, executedQty, entryFee);

            return (true, position.Id, order.Id);
        }
        catch (Exception ex)
        {
            var decisionRepo = sp.GetRequiredService<ITradeExecutionDesicionsRepository>();
            await decisionRepo.UpdateDesicionAsync(new TradeExecutionDecisions
            {
                Id = decisionId,
                CorrelationId = correlationId,
                DecisionStatus = DecisionStatus.Failed,
                GuardStage = GuardStage.Execution,
                ExecutionSuccess = false,
                ExecutionError = Truncate($"OrderPlacementFailed: {ex.Message}", 4000)
            });
            logger.LogError(ex, "SpotFuturesCrossMarket entry order placement failed. CorrelationId={CorrelationId}", correlationId);
            return (false, null, null);
        }
    }

    // --------------------------------------------------------------------- exits

    /// <summary>Signal-driven close decided on a fully closed candle (trend flip on either market).</summary>
    private async Task<(bool Executed, long? LocalOrderId)> TryCloseBySignalAsync(
        SpotFuturesCrossMarketSettings settings,
        IServiceProvider sp,
        Position openPosition,
        CrossMarketDecision decision,
        string correlationId,
        CancellationToken ct)
    {
        var order = await ExecuteCloseAsync(
            settings, sp, openPosition,
            PositionExitReason.OppositeSignal, CloseReason.OppositeSignal,
            decision.Reason, correlationId, ct);

        var status = order is not null ? DecisionStatus.Executed : DecisionStatus.Failed;
        await PersistDecisionAsync(sp, settings, decision, correlationId, status, GuardStage.Execution,
            executionSuccess: order is not null, localOrderId: order?.Id, exchangeOrderId: order?.ExchangeOrderId,
            error: order is null ? "CloseOrderPlacementFailed" : null);

        return (order is not null, order?.Id);
    }

    /// <summary>Intra-candle stop-loss / take-profit / max-hold checks against the live mark price.</summary>
    private async Task<Position?> TryProtectiveExitAsync(
        SpotFuturesCrossMarketSettings settings,
        IServiceProvider sp,
        Position openPosition,
        CancellationToken ct)
    {
        var futuresClient = sp.GetRequiredService<IFuturesTestnetClient>();
        var accounting = sp.GetRequiredService<SpotFuturesCrossMarketAccounting>();

        var markPrice = await futuresClient.GetMarkPriceAsync(openPosition.Symbol.ToString(), ct);
        if (markPrice <= 0m)
        {
            logger.LogWarning("SpotFuturesCrossMarket exit check skipped: mark price unavailable for {Symbol}", openPosition.Symbol);
            return openPosition;
        }

        var exitReason = ResolveProtectiveExitReason(settings, openPosition, markPrice, out var closeReason);
        if (exitReason is null)
        {
            await accounting.UpdateUnrealizedAsync(openPosition, markPrice, ct);
            logger.LogInformation(
                "SpotFuturesCrossMarket position open (no protective exit). PositionId={PositionId} Side={Side} EntryPrice={EntryPrice} MarkPrice={MarkPrice} UnrealizedPnl={Unrealized}",
                openPosition.Id, openPosition.Side, openPosition.AveragePrice, markPrice, openPosition.UnrealizedPnl);
            return openPosition;
        }

        var correlationId = NewId();
        var order = await ExecuteCloseAsync(
            settings, sp, openPosition, exitReason.Value, closeReason,
            $"Protective exit ({exitReason}) at mark price {markPrice}.", correlationId, ct);

        if (order is null)
            return openPosition;

        // Record the protective close as a decision row too (keeps the audit trail complete).
        var intent = openPosition.Side == OrderSide.BUY ? TradeExecutionIntent.CloseLong : TradeExecutionIntent.CloseShort;
        var decisionRepo = sp.GetRequiredService<ITradeExecutionDesicionsRepository>();
        await decisionRepo.AddDesicionAsync(new TradeExecutionDecisions
        {
            CorrelationId = correlationId,
            DecisionId = NewId(),
            StrategyName = StrategyName,
            Symbol = settings.Symbol,
            Action = intent == TradeExecutionIntent.CloseLong ? TradeSignal.Sell : TradeSignal.Buy,
            RawSignal = intent == TradeExecutionIntent.CloseLong ? TradeSignal.Sell : TradeSignal.Buy,
            TradingMode = TradingMode.Futures,
            ExecutionIntent = intent,
            Side = intent == TradeExecutionIntent.CloseLong ? OrderSide.SELL : OrderSide.BUY,
            DecisionStatus = DecisionStatus.Executed,
            GuardStage = GuardStage.Execution,
            Reason = $"Protective exit ({exitReason}). MarkPrice={markPrice}",
            ExecutionSuccess = true,
            LocalOrderId = order.Id,
            ExchangeOrderId = order.ExchangeOrderId
        });

        return null;
    }

    /// <summary>Places the reduce-only market close, persists order/fills, and settles the position.</summary>
    private async Task<Order?> ExecuteCloseAsync(
        SpotFuturesCrossMarketSettings settings,
        IServiceProvider sp,
        Position openPosition,
        PositionExitReason exitReason,
        CloseReason closeReason,
        string reason,
        string correlationId,
        CancellationToken ct)
    {
        var closeService = sp.GetRequiredService<SpotFuturesCrossMarketCloseOrderService>();
        var result = await closeService.CloseAsync(
            new SpotFuturesCrossMarketCloseRequest(
                settings,
                openPosition,
                exitReason,
                closeReason,
                reason,
                correlationId,
                OrderSource.SpotFuturesCrossMarketTestnetV1,
                DateTime.UtcNow),
            ct);

        return result.Order;
    }

    private static PositionExitReason? ResolveProtectiveExitReason(
        SpotFuturesCrossMarketSettings settings,
        Position position,
        decimal markPrice,
        out CloseReason closeReason)
    {
        closeReason = CloseReason.None;
        var isLong = position.Side == OrderSide.BUY;

        if (position.StopLossPrice.HasValue &&
            (isLong ? markPrice <= position.StopLossPrice.Value : markPrice >= position.StopLossPrice.Value))
        {
            closeReason = CloseReason.StopLoss;
            return PositionExitReason.StopLoss;
        }

        if (position.TakeProfitPrice.HasValue &&
            (isLong ? markPrice >= position.TakeProfitPrice.Value : markPrice <= position.TakeProfitPrice.Value))
        {
            closeReason = CloseReason.TakeProfit;
            return PositionExitReason.TakeProfit;
        }

        if (position.OpenedAt.HasValue &&
            (DateTime.UtcNow - position.OpenedAt.Value).TotalMinutes >= settings.MaxHoldMinutes)
        {
            closeReason = CloseReason.MaxDuration;
            return PositionExitReason.Time;
        }

        return null;
    }

    // --------------------------------------------------------------------- sizing

    /// <summary>Notional / mark price, floored to the exchange step size and validated against exchange minimums.</summary>
    private async Task<decimal> ResolveQuantityAsync(
        SpotFuturesCrossMarketSettings settings,
        IServiceProvider sp,
        IFuturesTestnetClient futuresClient,
        decimal markPrice,
        CancellationToken ct)
    {
        if (!_symbolFilters.TryGetValue(settings.Symbol, out var filters))
        {
            var redis = sp.GetRequiredService<IRedisCacheService>();
            var cacheKey = $"FuturesTestnet:ExchangeFilters:{settings.Symbol}";
            filters = await redis.GetCacheValue<FuturesTestnetSymbolFilters>(cacheKey);
            if (filters is null || filters.QuantityStepSize <= 0m)
            {
                filters = await futuresClient.GetSymbolFiltersAsync(settings.Symbol.ToString(), ct);
                if (filters.QuantityStepSize > 0m)
                    await redis.SetCacheValue(cacheKey, filters);
            }

            _symbolFilters[settings.Symbol] = filters;
        }

        var notional = await ResolveNotionalAsync(settings, futuresClient, ct);

        var rawQuantity = notional / markPrice;
        var step = filters.QuantityStepSize > 0m ? filters.QuantityStepSize : 0.001m;
        var quantity = Math.Floor(rawQuantity / step) * step;

        if (filters.MinQuantity > 0m && quantity < filters.MinQuantity)
            return 0m;
        if (filters.MinNotional > 0m && quantity * markPrice < filters.MinNotional)
            return 0m;

        return quantity;
    }

    /// <summary>
    /// Target notional (USDT) for one entry. With balance-based sizing:
    /// margin = wallet balance * (BalanceAllocationPercent / 100) / number of symbols,
    /// notional = margin * Leverage. Falls back to the fixed NotionalUsdt when disabled
    /// or when the balance cannot be fetched.
    /// </summary>
    private async Task<decimal> ResolveNotionalAsync(
        SpotFuturesCrossMarketSettings settings,
        IFuturesTestnetClient futuresClient,
        CancellationToken ct)
    {
        if (!settings.UseBalanceBasedSizing)
            return settings.NotionalUsdt;

        try
        {
            var balance = await futuresClient.GetBalanceAsync("USDT", ct);
            var marginPerSymbol = balance.WalletBalance * (settings.BalanceAllocationPercent / 100m) / Math.Max(1, settings.Symbols.Count);
            var notional = marginPerSymbol * settings.Leverage;

            logger.LogInformation(
                "SpotFuturesCrossMarket sizing. Symbol={Symbol} WalletBalance={WalletBalance:F2} AllocationPercent={AllocationPercent} SymbolCount={SymbolCount} MarginPerSymbol={MarginPerSymbol:F2} Leverage={Leverage} Notional={Notional:F2}",
                settings.Symbol, balance.WalletBalance, settings.BalanceAllocationPercent, settings.Symbols.Count,
                marginPerSymbol, settings.Leverage, notional);

            return notional > 0m ? notional : settings.NotionalUsdt;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SpotFuturesCrossMarket balance fetch failed; falling back to fixed notional {Notional} USDT.", settings.NotionalUsdt);
            return settings.NotionalUsdt;
        }
    }

    // --------------------------------------------------------------------- persistence helpers

    private static async Task<long> PersistDecisionAsync(
        IServiceProvider sp,
        SpotFuturesCrossMarketSettings settings,
        CrossMarketDecision decision,
        string correlationId,
        DecisionStatus status,
        GuardStage guardStage,
        bool executionSuccess,
        long? localOrderId,
        long? exchangeOrderId,
        string? error)
    {
        var decisionRepo = sp.GetRequiredService<ITradeExecutionDesicionsRepository>();
        var intent = decision.ToExecutionIntent();

        var (action, side) = intent switch
        {
            TradeExecutionIntent.OpenLong => (TradeSignal.Buy, OrderSide.BUY),
            TradeExecutionIntent.CloseShort => (TradeSignal.Buy, OrderSide.BUY),
            TradeExecutionIntent.OpenShort => (TradeSignal.Sell, OrderSide.SELL),
            TradeExecutionIntent.CloseLong => (TradeSignal.Sell, OrderSide.SELL),
            _ => (TradeSignal.Hold, OrderSide.BUY)
        };

        return await decisionRepo.AddDesicionAsync(new TradeExecutionDecisions
        {
            CorrelationId = correlationId,
            DecisionId = NewId(),
            StrategyName = StrategyName,
            Symbol = settings.Symbol,
            Action = action,
            RawSignal = action,
            TradingMode = TradingMode.Futures,
            ExecutionIntent = intent,
            Side = side,
            DecisionStatus = status,
            GuardStage = guardStage,
            RiskIsAllowed = guardStage != GuardStage.Risk,
            Reason = Truncate(decision.Reason, 4000),
            StopLossPrice = decision.StopLossPrice,
            TakeProfitPrice = decision.TakeProfitPrice,
            ExpectedMovePercent = decision.ExpectedMovePercent,
            TrendConfidenceScore = decision.FuturesTrendConfidenceScore,
            ShortMaSlopePercent = decision.FuturesShortMaSlopePercent,
            TrendStrengthPercent = decision.FuturesTrendStrengthPercent,
            ExecutionSuccess = executionSuccess,
            LocalOrderId = localOrderId,
            ExchangeOrderId = exchangeOrderId,
            ExecutionError = error is null ? null : Truncate(error, 4000)
        });
    }

    private async Task PersistEvaluationAsync(
        IServiceProvider sp,
        SpotFuturesCrossMarketSettings settings,
        CrossMarketSnapshot snapshot,
        CrossMarketDecision decision,
        string correlationId,
        bool executed,
        long? positionId,
        long? localOrderId,
        CancellationToken ct)
    {
        var evaluation = new SpotFuturesCrossMarketEvaluation
        {
            CorrelationId = correlationId,
            Symbol = settings.Symbol,
            Interval = settings.Interval,
            CandleOpenTimeUtc = snapshot.CandleOpenTimeUtc,
            CandleCloseTimeUtc = snapshot.CandleCloseTimeUtc,
            SpotClose = snapshot.SpotClose,
            SpotTrendState = decision.SpotTrendState,
            SpotTrendConfidenceScore = decision.SpotTrendConfidenceScore,
            SpotShortMaSlopePercent = decision.SpotShortMaSlopePercent,
            SpotTrendStrengthPercent = decision.SpotTrendStrengthPercent,
            SpotMomentumPercent = decision.SpotMomentumPercent,
            FuturesClose = snapshot.FuturesClose,
            FuturesTrendState = decision.FuturesTrendState,
            FuturesTrendConfidenceScore = decision.FuturesTrendConfidenceScore,
            FuturesShortMaSlopePercent = decision.FuturesShortMaSlopePercent,
            FuturesTrendStrengthPercent = decision.FuturesTrendStrengthPercent,
            FuturesAtrPercent = decision.FuturesAtrPercent,
            BasisPercent = snapshot.BasisPercent,
            FundingRate = snapshot.FundingRate,
            MarkPrice = snapshot.MarkPrice,
            MarketsInSync = snapshot.MarketsInSync,
            DecidedIntent = decision.ToExecutionIntent(),
            DecisionLabel = decision.Action.ToString(),
            Reason = Truncate(decision.Reason, 4000),
            Executed = executed,
            PositionId = positionId,
            LocalOrderId = localOrderId
        };

        var evaluationRepo = sp.GetRequiredService<ISpotFuturesCrossMarketEvaluationRepository>();
        await evaluationRepo.InsertAsync(evaluation, ct);

        var redis = sp.GetRequiredService<IRedisCacheService>();
        await redis.SetCacheValue(LastEvaluationRedisKey(settings), evaluation);
    }

    private static async Task PersistExecutionsAsync(
        IServiceProvider sp,
        Order order,
        IReadOnlyList<FuturesTestnetUserTrade> fills,
        FuturesTestnetOrderResult result,
        TradingSymbol symbol,
        OrderSide side,
        decimal fallbackPrice,
        decimal fallbackQty,
        CancellationToken ct)
    {
        var executionRepo = sp.GetRequiredService<ITradeExecutionRepository>();
        var now = DateTime.UtcNow;

        if (fills.Count > 0)
        {
            foreach (var fill in fills)
            {
                await executionRepo.InsertAsync(new TradeExecution
                {
                    OrderId = order.Id,
                    ExchangeOrderId = order.ExchangeOrderId,
                    ExchangeTradeId = fill.Id,
                    Symbol = symbol,
                    Side = side,
                    Price = fill.Price,
                    Quantity = fill.Qty,
                    QuoteQuantity = fill.QuoteQty > 0m ? fill.QuoteQty : fill.Price * fill.Qty,
                    Fee = fill.Commission,
                    FeeAsset = string.IsNullOrWhiteSpace(fill.CommissionAsset) ? "USDT" : fill.CommissionAsset,
                    PositionProcessedAt = now,
                    ExecutedAt = fill.TimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(fill.TimeMs).UtcDateTime : now
                }, ct);
            }

            return;
        }

        await executionRepo.InsertAsync(new TradeExecution
        {
            OrderId = order.Id,
            ExchangeOrderId = order.ExchangeOrderId,
            ExchangeTradeId = null,
            Symbol = symbol,
            Side = side,
            Price = fallbackPrice,
            Quantity = fallbackQty,
            QuoteQuantity = result.CumQuote > 0m ? result.CumQuote : fallbackPrice * fallbackQty,
            Fee = 0m,
            FeeAsset = "USDT",
            PositionProcessedAt = now,
            ExecutedAt = result.UpdateTimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(result.UpdateTimeMs).UtcDateTime : now
        }, ct);
    }

    // --------------------------------------------------------------------- misc helpers

    private bool IsInReentryCooldown(
        SpotFuturesCrossMarketSettings settings,
        IReadOnlyList<Position> closed,
        DateTime candleOpenUtc)
    {
        if (settings.ReentryCooldownCandles <= 0)
            return false;

        var lastClose = closed
            .Where(p => p.Symbol == settings.Symbol && p.ClosedAt.HasValue)
            .Select(p => p.ClosedAt!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        if (lastClose == DateTime.MinValue)
            return false;

        var cooldownUntil = lastClose + settings.IntervalTimeSpan * settings.ReentryCooldownCandles;
        return candleOpenUtc < cooldownUntil;
    }

    /// <summary>
    /// Resolves the authoritative fill data for a just-placed market order. The immediate
    /// order response often reports avgPrice=0 / no fills before the match propagates, so
    /// this retries userTrades and falls back to re-querying the order itself. Recording an
    /// entry/exit at a stale price would corrupt every downstream PnL number.
    /// </summary>
    private async Task<(IReadOnlyList<FuturesTestnetUserTrade> Fills, decimal AvgPrice, decimal ExecutedQty, decimal Fee)> ResolveFillAsync(
        IFuturesTestnetClient client,
        string symbol,
        FuturesTestnetOrderResult placement,
        CancellationToken ct)
    {
        IReadOnlyList<FuturesTestnetUserTrade> fills = Array.Empty<FuturesTestnetUserTrade>();

        for (var attempt = 0; attempt < 4 && fills.Count == 0; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);

            try
            {
                fills = await client.GetUserTradesAsync(symbol, placement.OrderId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SpotFuturesCrossMarket fill query failed (attempt {Attempt}). OrderId={OrderId}", attempt + 1, placement.OrderId);
            }
        }

        if (fills.Count > 0)
            return (fills, Vwap(fills), fills.Sum(f => f.Qty), fills.Sum(f => f.Commission));

        if (placement.AvgPrice > 0m)
            return (fills, placement.AvgPrice, placement.ExecutedQty, 0m);

        try
        {
            var order = await client.GetOrderAsync(symbol, placement.OrderId, ct);
            if (order.AvgPrice > 0m)
            {
                logger.LogInformation(
                    "SpotFuturesCrossMarket fill resolved via order re-query. OrderId={OrderId} AvgPrice={AvgPrice} ExecutedQty={ExecutedQty}",
                    placement.OrderId, order.AvgPrice, order.ExecutedQty);
                return (fills, order.AvgPrice, order.ExecutedQty, 0m);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SpotFuturesCrossMarket order re-query failed. OrderId={OrderId}", placement.OrderId);
        }

        logger.LogWarning(
            "SpotFuturesCrossMarket could not resolve fill price for OrderId={OrderId}; caller will fall back to a reference price. Recorded PnL may be inexact for this trade.",
            placement.OrderId);
        return (fills, 0m, placement.ExecutedQty, 0m);
    }

    private static int TrailingConsecutiveLosses(IReadOnlyList<Position> closed)
    {
        var count = 0;
        foreach (var p in closed.OrderByDescending(p => p.ClosedAt ?? p.UpdatedAt).ThenByDescending(p => p.Id))
        {
            if (p.RealizedPnl < 0m)
                count++;
            else
                break;
        }

        return count;
    }

    private static int CountTradesToday(IReadOnlyList<Position> closed)
    {
        var today = DateTime.UtcNow.Date;
        return closed.Count(p => (p.OpenedAt ?? p.CreatedAt).Date == today);
    }

    private static decimal Vwap(IReadOnlyList<FuturesTestnetUserTrade> fills)
    {
        decimal quote = 0m, qty = 0m;
        foreach (var f in fills)
        {
            if (f.Price <= 0m || f.Qty <= 0m)
                continue;
            quote += f.Price * f.Qty;
            qty += f.Qty;
        }

        return qty > 0m ? quote / qty : 0m;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
