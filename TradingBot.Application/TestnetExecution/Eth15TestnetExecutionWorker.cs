using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Decision;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.TestnetExecution;

/// <summary>
/// ETH15 fixed-frequency forward-incubation Binance Futures Testnet execution worker.
///
/// Testnet-validation only. Real-money trading is impossible: the worker refuses to act unless
/// the safety settings explicitly allow testnet orders, every gate derived from the frozen
/// incubation outputs passes, and the dedicated testnet client (bound to the testnet host) is
/// used. It reuses the existing order/position/trade-execution/decision repositories and tags
/// every row with the testnet execution environment so the live Spot pipeline never sees it.
/// </summary>
public sealed class Eth15TestnetExecutionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<Eth15TestnetExecutionWorker> logger) : BackgroundService
{
    private const int QuantityDecimals = 3; // ETHUSDT USD-M futures quantity precision.

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Eth15TestnetExecutionSettings settings;
        try
        {
            settings = Eth15TestnetExecutionSettings.Load(configuration, hostEnvironment.ContentRootPath);
            settings.ValidateTestnetSafety(
                configuration.GetValue<string>("ApiKey"),
                configuration.GetValue<string>("SecretKey"));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogCritical(ex, "Eth15TestnetExecution forbidden/unsafe configuration detected. Worker will not run.");
            throw;
        }

        if (!settings.Enabled)
        {
            logger.LogInformation("Eth15TestnetExecution disabled by config. Worker not running.");
            return;
        }

        logger.LogInformation(
            "Eth15TestnetExecution worker started. AllowTestnetOrders={AllowTestnetOrders} CanPlaceTestnetOrders={CanPlace} Notional={Notional} Leverage={Leverage} MaxOpen={MaxOpen} DailyMax={DailyMax} MaxConsecLosses={MaxConsec} IntervalSeconds={Interval} IncubationDir={IncubationDir}",
            settings.AllowTestnetOrders,
            settings.CanPlaceTestnetOrders,
            settings.NotionalUsdt,
            settings.Leverage,
            settings.MaxOpenPositions,
            settings.DailyMaxTrades,
            settings.MaxConsecutiveLosses,
            settings.IntervalSeconds,
            settings.IncubationOutputDirectory);

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
            catch (InvalidOperationException ex) when (
                ex.Message.StartsWith("Eth15TestnetExecution", StringComparison.Ordinal))
            {
                logger.LogCritical(ex, "Eth15TestnetExecution forbidden/unsafe configuration detected at runtime. Stopping worker.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Eth15TestnetExecution cycle failed at {Time}", DateTime.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        logger.LogInformation("Eth15TestnetExecution worker stopped.");
    }

    private async Task RunCycleAsync(Eth15TestnetExecutionSettings settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var orderRepo = sp.GetRequiredService<IOrderRepository>();
        var positionRepo = sp.GetRequiredService<IPositionRepository>();
        var executionRepo = sp.GetRequiredService<ITradeExecutionRepository>();
        var decisionRepo = sp.GetRequiredService<ITradeExecutionDesicionsRepository>();
        var futuresClient = sp.GetRequiredService<IFuturesTestnetClient>();
        var gateEvaluator = sp.GetRequiredService<Eth15TestnetGateEvaluator>();
        var accounting = sp.GetRequiredService<Eth15TestnetShortAccounting>();
        var reportWriter = sp.GetRequiredService<Eth15TestnetReportWriter>();

        const string env = Eth15TestnetExecutionSettings.ExecutionEnvironment;

        Eth15GateDecision? lastGate = null;

        var openPositions = await positionRepo.GetOpenPositionsByEnvironmentAsync(env, cancellationToken);
        var openShort = openPositions.FirstOrDefault(p => p.Side == OrderSide.SELL && p.IsOpen);

        if (openShort is not null)
        {
            await TryExitOpenPositionAsync(settings, futuresClient, orderRepo, executionRepo, decisionRepo, accounting, openShort, cancellationToken);
        }
        else
        {
            lastGate = await TryEnterAsync(settings, gateEvaluator, futuresClient, orderRepo, positionRepo, executionRepo, decisionRepo, accounting, cancellationToken);
        }

        // Reporting (always refresh from the testnet-scoped rows).
        var closed = await positionRepo.GetClosedPositionsByEnvironmentAsync(env, cancellationToken);
        var openNow = await positionRepo.GetOpenPositionsByEnvironmentAsync(env, cancellationToken);
        await reportWriter.WriteAsync(settings.ReportOutputDirectory, closed, openNow, lastGate, cancellationToken);
    }

    private async Task<Eth15GateDecision?> TryEnterAsync(
        Eth15TestnetExecutionSettings settings,
        Eth15TestnetGateEvaluator gateEvaluator,
        IFuturesTestnetClient futuresClient,
        IOrderRepository orderRepo,
        IPositionRepository positionRepo,
        ITradeExecutionRepository executionRepo,
        ITradeExecutionDesicionsRepository decisionRepo,
        Eth15TestnetShortAccounting accounting,
        CancellationToken cancellationToken)
    {
        const string env = Eth15TestnetExecutionSettings.ExecutionEnvironment;
        var symbol = Eth15TestnetExecutionSettings.Symbol;

        var gate = gateEvaluator.Evaluate(settings.IncubationOutputDirectory);

        // Runtime safety guards (independent of the research gates).
        var closed = await positionRepo.GetClosedPositionsByEnvironmentAsync(env, cancellationToken);
        var openCount = (await positionRepo.GetOpenPositionsByEnvironmentAsync(env, cancellationToken)).Count;
        var consecutiveLosses = TrailingConsecutiveLosses(closed);
        var tradesToday = CountTradesToday(closed) + openCount;

        string? guardBlock = null;
        var guardStage = GuardStage.None;

        if (!settings.CanPlaceTestnetOrders)
        {
            guardBlock = "TestnetOrdersNotAllowed(Enabled/AllowTestnetOrders must both be true)";
            guardStage = GuardStage.Execution;
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

        if (guardBlock is not null)
        {
            await PersistBlockedDecisionAsync(decisionRepo, gate, guardBlock, guardStage, cancellationToken);
            logger.LogInformation(
                "ETH15 testnet entry blocked (runtime guard). Reason={Reason} Verdict={Verdict} EntryPresent={EntryPresent} ConsecutiveLosses={ConsecLosses} TradesToday={TradesToday} OpenCount={OpenCount}",
                guardBlock, gate.Verdict, gate.EntryPresent, consecutiveLosses, tradesToday, openCount);
            return gate;
        }

        if (!gate.CanPlaceOrder)
        {
            await PersistBlockedDecisionAsync(decisionRepo, gate, gate.BlockedReason, GuardStage.None, cancellationToken);
            logger.LogInformation(
                "ETH15 testnet entry blocked (gate). Verdict={Verdict} ActivationPassed={Activation} EntryPresent={EntryPresent} Parked={Parked} StressPlusPositive={StressPlus} AllGatesPass={AllGates} Reason={Reason}",
                gate.Verdict, gate.ActivationPassed, gate.EntryPresent, gate.Parked, gate.StressPlusPositive, gate.AllHealthGatesPass, gate.BlockedReason);
            return gate;
        }

        // All gates and guards passed: place the testnet short entry.
        var correlationId = NewId();
        var decision = BuildBaseDecision(gate, correlationId);
        decision.DecisionStatus = DecisionStatus.Pending;
        decision.Reason = $"All gates passed; placing testnet short. Verdict={gate.Verdict}; NetStressPlus={gate.NetStressPlus}";
        var decisionId = await decisionRepo.AddDesicionAsync(decision);

        var profile = gate.Profile;
        var markPrice = await futuresClient.GetMarkPriceAsync(symbol.ToString(), cancellationToken);
        if (markPrice <= 0m)
        {
            await MarkDecisionFailedAsync(decisionRepo, decisionId, correlationId, "MarkPriceUnavailable", cancellationToken);
            logger.LogWarning("ETH15 testnet entry aborted: mark price unavailable for {Symbol}", symbol);
            return gate;
        }

        var quantity = Math.Round(settings.NotionalUsdt / markPrice, QuantityDecimals, MidpointRounding.AwayFromZero);
        if (quantity <= 0m)
        {
            await MarkDecisionFailedAsync(decisionRepo, decisionId, correlationId, $"NotionalTooSmall(notional={settings.NotionalUsdt}, price={markPrice})", cancellationToken);
            logger.LogWarning("ETH15 testnet entry aborted: computed quantity is zero. Notional={Notional} Price={Price}", settings.NotionalUsdt, markPrice);
            return gate;
        }

        var stopPercent = profile?.StopPercent ?? 0.75m;
        var targetPercent = profile?.TargetPercent ?? 1.25m;
        var stopLossPrice = markPrice * (1m + stopPercent / 100m);     // short stop is above entry
        var takeProfitPrice = markPrice * (1m - targetPercent / 100m); // short target is below entry

        logger.LogInformation(
            "ETH15 testnet ORDER INTENT (entry). Symbol={Symbol} Side=SELL Quantity={Quantity} MarkPrice={MarkPrice} Notional={Notional} Leverage={Leverage} StopLoss={StopLoss} TakeProfit={TakeProfit} CorrelationId={CorrelationId}",
            symbol, quantity, markPrice, settings.NotionalUsdt, settings.Leverage, stopLossPrice, takeProfitPrice, correlationId);

        try
        {
            await futuresClient.EnsureLeverageAsync(symbol.ToString(), settings.Leverage, cancellationToken);
            var result = await futuresClient.PlaceMarketOrderAsync(symbol.ToString(), OrderSide.SELL, quantity, reduceOnly: false, cancellationToken);

            var fills = await SafeGetFillsAsync(futuresClient, symbol.ToString(), result.OrderId, cancellationToken);
            var entryPrice = result.AvgPrice > 0m ? result.AvgPrice : (fills.Count > 0 ? Vwap(fills) : markPrice);
            var executedQty = result.ExecutedQty > 0m ? result.ExecutedQty : quantity;
            var entryFee = fills.Sum(f => f.Commission);

            var order = new Order
            {
                ExchangeOrderId = result.OrderId,
                CorrelationId = correlationId,
                OrderSource = OrderSource.Eth15TestnetExecution,
                CloseReason = CloseReason.None,
                Symbol = symbol,
                Side = OrderSide.SELL,
                Status = result.Status.ToOrderStatus(),
                ProcessingStatus = ProcessingStatus.PositionUpdated,
                Price = entryPrice,
                Quantity = executedQty,
                ExecutionEnvironment = env
            };
            await orderRepo.InsertAsync(order, cancellationToken);

            await PersistExecutionsAsync(executionRepo, order, fills, result, symbol, OrderSide.SELL, entryPrice, executedQty, cancellationToken);

            var position = await accounting.OpenShortAsync(
                symbol, executedQty, entryPrice, entryFee, stopLossPrice, takeProfitPrice, DateTime.UtcNow, cancellationToken);

            order.ParentPositionId = position.Id;
            await orderRepo.UpdateAsync(order, cancellationToken);

            await MarkDecisionExecutedAsync(decisionRepo, decisionId, correlationId, order, stopLossPrice, takeProfitPrice, cancellationToken);

            logger.LogInformation(
                "ETH15 testnet ENTRY FILLED. PositionId={PositionId} LocalOrderId={LocalOrderId} ExchangeOrderId={ExchangeOrderId} EntryPrice={EntryPrice} Quantity={Quantity} EntryFee={EntryFee}",
                position.Id, order.Id, order.ExchangeOrderId, entryPrice, executedQty, entryFee);
        }
        catch (Exception ex)
        {
            await MarkDecisionFailedAsync(decisionRepo, decisionId, correlationId, $"OrderPlacementFailed: {ex.Message}", cancellationToken);
            logger.LogError(ex, "ETH15 testnet entry order placement failed. CorrelationId={CorrelationId}", correlationId);
        }

        return gate;
    }

    private async Task TryExitOpenPositionAsync(
        Eth15TestnetExecutionSettings settings,
        IFuturesTestnetClient futuresClient,
        IOrderRepository orderRepo,
        ITradeExecutionRepository executionRepo,
        ITradeExecutionDesicionsRepository decisionRepo,
        Eth15TestnetShortAccounting accounting,
        Position openShort,
        CancellationToken cancellationToken)
    {
        var symbol = openShort.Symbol;
        var markPrice = await futuresClient.GetMarkPriceAsync(symbol.ToString(), cancellationToken);
        if (markPrice <= 0m)
        {
            logger.LogWarning("ETH15 testnet exit check skipped: mark price unavailable for {Symbol}", symbol);
            return;
        }

        var exitReason = ResolveExitReason(settings, openShort, markPrice, out var closeReason);
        if (exitReason is null)
        {
            // Still open; refresh unrealized PnL for reporting.
            await accounting.UpdateUnrealizedAsync(openShort, markPrice, cancellationToken);
            logger.LogInformation(
                "ETH15 testnet position open (no exit). PositionId={PositionId} EntryPrice={EntryPrice} MarkPrice={MarkPrice} UnrealizedPnl={Unrealized}",
                openShort.Id, openShort.AveragePrice, markPrice, openShort.UnrealizedPnl);
            return;
        }

        var correlationId = NewId();
        logger.LogInformation(
            "ETH15 testnet ORDER INTENT (exit). PositionId={PositionId} Symbol={Symbol} Side=BUY Quantity={Quantity} MarkPrice={MarkPrice} ExitReason={ExitReason} CorrelationId={CorrelationId}",
            openShort.Id, symbol, openShort.Quantity, markPrice, exitReason, correlationId);

        try
        {
            var result = await futuresClient.PlaceMarketOrderAsync(symbol.ToString(), OrderSide.BUY, openShort.Quantity, reduceOnly: true, cancellationToken);
            var fills = await SafeGetFillsAsync(futuresClient, symbol.ToString(), result.OrderId, cancellationToken);
            var exitPrice = result.AvgPrice > 0m ? result.AvgPrice : (fills.Count > 0 ? Vwap(fills) : markPrice);
            var executedQty = result.ExecutedQty > 0m ? result.ExecutedQty : openShort.Quantity;
            var exitFee = fills.Sum(f => f.Commission);

            var order = new Order
            {
                ExchangeOrderId = result.OrderId,
                CorrelationId = correlationId,
                ParentPositionId = openShort.Id,
                OrderSource = OrderSource.Eth15TestnetExecution,
                CloseReason = closeReason,
                Symbol = symbol,
                Side = OrderSide.BUY,
                Status = result.Status.ToOrderStatus(),
                ProcessingStatus = ProcessingStatus.PositionUpdated,
                Price = exitPrice,
                Quantity = executedQty,
                ExecutionEnvironment = Eth15TestnetExecutionSettings.ExecutionEnvironment
            };
            await orderRepo.InsertAsync(order, cancellationToken);

            await PersistExecutionsAsync(executionRepo, order, fills, result, symbol, OrderSide.BUY, exitPrice, executedQty, cancellationToken);

            var closedPosition = await accounting.CloseShortAsync(openShort, exitPrice, exitFee, exitReason.Value, DateTime.UtcNow, cancellationToken);

            var decision = new TradeExecutionDecisions
            {
                CorrelationId = correlationId,
                DecisionId = NewId(),
                StrategyName = Eth15TestnetExecutionSettings.ProfileName,
                Symbol = symbol,
                Action = TradeSignal.Buy,
                RawSignal = TradeSignal.Buy,
                TradingMode = TradingMode.Futures,
                ExecutionIntent = TradeExecutionIntent.CloseShort,
                Side = OrderSide.BUY,
                DecisionStatus = DecisionStatus.Executed,
                GuardStage = GuardStage.Execution,
                Reason = $"Testnet short exit ({exitReason}). ExitPrice={exitPrice}; RealizedPnl={closedPosition.RealizedPnl}",
                ExecutionSuccess = true,
                LocalOrderId = order.Id,
                ExchangeOrderId = order.ExchangeOrderId
            };
            await decisionRepo.AddDesicionAsync(decision);

            logger.LogInformation(
                "ETH15 testnet EXIT FILLED. PositionId={PositionId} ExitReason={ExitReason} ExitPrice={ExitPrice} RealizedPnl={RealizedPnl} ExitFee={ExitFee} LocalOrderId={LocalOrderId} ExchangeOrderId={ExchangeOrderId}",
                closedPosition.Id, exitReason, exitPrice, closedPosition.RealizedPnl, exitFee, order.Id, order.ExchangeOrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ETH15 testnet exit order placement failed. PositionId={PositionId} CorrelationId={CorrelationId}", openShort.Id, correlationId);
        }
    }

    private static PositionExitReason? ResolveExitReason(
        Eth15TestnetExecutionSettings settings,
        Position openShort,
        decimal markPrice,
        out CloseReason closeReason)
    {
        closeReason = CloseReason.None;

        if (openShort.StopLossPrice.HasValue && markPrice >= openShort.StopLossPrice.Value)
        {
            closeReason = CloseReason.StopLoss;
            return PositionExitReason.StopLoss;
        }

        if (openShort.TakeProfitPrice.HasValue && markPrice <= openShort.TakeProfitPrice.Value)
        {
            closeReason = CloseReason.TakeProfit;
            return PositionExitReason.TakeProfit;
        }

        if (openShort.OpenedAt.HasValue)
        {
            var heldMinutes = (DateTime.UtcNow - openShort.OpenedAt.Value).TotalMinutes;
            if (heldMinutes >= settings.MaxHoldMinutes)
            {
                closeReason = CloseReason.MaxDuration;
                return PositionExitReason.Time;
            }
        }

        return null;
    }

    private static async Task PersistExecutionsAsync(
        ITradeExecutionRepository executionRepo,
        Order order,
        IReadOnlyList<FuturesTestnetUserTrade> fills,
        FuturesTestnetOrderResult result,
        TradingSymbol symbol,
        OrderSide side,
        decimal fallbackPrice,
        decimal fallbackQty,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        if (fills.Count > 0)
        {
            foreach (var fill in fills)
            {
                var execution = new TradeExecution
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
                };
                await executionRepo.InsertAsync(execution, cancellationToken);
            }

            return;
        }

        // No per-trade detail available: synthesize one execution from the order result.
        var synthetic = new TradeExecution
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
        };
        await executionRepo.InsertAsync(synthetic, cancellationToken);
    }

    private static async Task<IReadOnlyList<FuturesTestnetUserTrade>> SafeGetFillsAsync(
        IFuturesTestnetClient client,
        string symbol,
        long orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetUserTradesAsync(symbol, orderId, cancellationToken);
        }
        catch
        {
            return Array.Empty<FuturesTestnetUserTrade>();
        }
    }

    private static TradeExecutionDecisions BuildBaseDecision(Eth15GateDecision gate, string correlationId) => new()
    {
        CorrelationId = correlationId,
        DecisionId = NewId(),
        StrategyName = Eth15TestnetExecutionSettings.ProfileName,
        Symbol = Eth15TestnetExecutionSettings.Symbol,
        Action = TradeSignal.Sell,
        RawSignal = TradeSignal.Sell,
        TradingMode = TradingMode.Futures,
        ExecutionIntent = TradeExecutionIntent.OpenShort,
        Side = OrderSide.SELL,
        VolatilityRegime = gate.Summary?.LatestStatus,
        ExecutionSuccess = false
    };

    private async Task PersistBlockedDecisionAsync(
        ITradeExecutionDesicionsRepository decisionRepo,
        Eth15GateDecision gate,
        string blockedReason,
        GuardStage guardStage,
        CancellationToken cancellationToken)
    {
        var decision = BuildBaseDecision(gate, NewId());
        decision.DecisionStatus = DecisionStatus.Skipped;
        decision.GuardStage = guardStage;
        decision.RiskIsAllowed = guardStage != GuardStage.Risk;
        decision.Reason = Truncate(blockedReason, 4000);
        decision.ExecutionError = "Blocked - no testnet order placed.";
        await decisionRepo.AddDesicionAsync(decision);
    }

    private static async Task MarkDecisionExecutedAsync(
        ITradeExecutionDesicionsRepository decisionRepo,
        long decisionId,
        string correlationId,
        Order order,
        decimal stopLossPrice,
        decimal takeProfitPrice,
        CancellationToken cancellationToken)
    {
        await decisionRepo.UpdateDesicionAsync(new TradeExecutionDecisions
        {
            Id = decisionId,
            CorrelationId = correlationId,
            DecisionStatus = DecisionStatus.Executed,
            GuardStage = GuardStage.Execution,
            ExecutionSuccess = true,
            LocalOrderId = order.Id,
            ExchangeOrderId = order.ExchangeOrderId,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice
        });
    }

    private static async Task MarkDecisionFailedAsync(
        ITradeExecutionDesicionsRepository decisionRepo,
        long decisionId,
        string correlationId,
        string error,
        CancellationToken cancellationToken)
    {
        await decisionRepo.UpdateDesicionAsync(new TradeExecutionDecisions
        {
            Id = decisionId,
            CorrelationId = correlationId,
            DecisionStatus = DecisionStatus.Failed,
            GuardStage = GuardStage.Execution,
            ExecutionSuccess = false,
            ExecutionError = Truncate(error, 4000)
        });
    }

    private static int TrailingConsecutiveLosses(IReadOnlyList<Position> closed)
    {
        var ordered = closed
            .OrderByDescending(p => p.ClosedAt ?? p.UpdatedAt)
            .ThenByDescending(p => p.Id)
            .ToList();

        var count = 0;
        foreach (var p in ordered)
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
