using System.Globalization;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Application.BackgroundHostService;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Binance;
using TradingBot.Domain.Models.Trading;
using TradingBot.Domain.Models.TradingEndpoints;
using TradingBot.Domain.Utilities;

namespace TradingBot.Application.Trading;

public class PlaceSpotOrderCommandHandler(
    IToolService toolService,
    IOrderRepository orderRepository,
    IPositionRepository positionRepository,
    ITradeExecutionRepository tradeExecutionRepository,
    IOrderStatusService orderStatusService,
    ITradeCooldownService tradeCooldownService,
    IRiskManagementService riskManagementService,
    IFeeProfitGuard feeProfitGuard,
    IBinanceOrderNormalizationService binanceOrderNormalizationService,
    ITimeSyncService timeSyncService,
    IPriceCacheService priceCacheService,
    IConfiguration configuration,
    ILogger<PlaceSpotOrderCommandHandler> logger) : IRequestHandler<PlaceSpotOrderCommand, PlaceSpotOrderResult>
{
    public async Task<PlaceSpotOrderResult> Handle(PlaceSpotOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolvedParentPositionId = request.ParentPositionId;
            if (!resolvedParentPositionId.HasValue)
            {
                var openPosition = await positionRepository.GetOpenPositionAsync(request.Symbol, cancellationToken);
                if (openPosition is not null)
                    resolvedParentPositionId = openPosition.Id;
            }

            if (request.Quantity <= 0)
            {
                return new PlaceSpotOrderResult
                {
                    Success = false,
                    Error = "Quantity must be greater than zero."
                };
            }

            if (request.IsLimitOrder && (request.Price == null || request.Price <= 0))
            {
                return new PlaceSpotOrderResult
                {
                    Success = false,
                    Error = "Limit orders require a positive price."
                };
            }

            var price = request.IsLimitOrder
            ? request.Price!.Value
            : (await priceCacheService.GetCachedPriceAsync(request.Symbol, cancellationToken) ?? await GetCurrentPrice(request.Symbol, cancellationToken));

            var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync(cancellationToken);

            var newOrderEndpoint = toolService.BinanceEndpointsService.GetEndpoint(TradingBot.Domain.Enums.Endpoints.Trading.NewOrder);

            var rawNewOrderRequest = new NewOrderRequest
            {
                Symbol = request.Symbol.ToString(),
                Side = request.Side,
                Type = request.IsLimitOrder ? OrderTypes.LIMIT : OrderTypes.MARKET,
                Quantity = request.Quantity,
                Price = request.IsLimitOrder ? price : null,
                TimeInForce = request.IsLimitOrder ? TimeInForce.GTC : null,
                Timestamp = adjustedTimestamp,
                RecvWindow = 30000
            };
            BinanceOrderNormalizationResult normalizedOrder;
            try
            {
                normalizedOrder = await binanceOrderNormalizationService.NormalizeNewOrderAsync(
                    rawNewOrderRequest,
                    price,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Order normalization failed before Binance call. Symbol={Symbol}, Side={Side}, Type={Type}, Quantity={Quantity}, Price={Price}",
                    rawNewOrderRequest.Symbol,
                    rawNewOrderRequest.Side,
                    rawNewOrderRequest.Type,
                    rawNewOrderRequest.Quantity,
                    rawNewOrderRequest.Price);

                return new PlaceSpotOrderResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }

            var normalizedRequest = normalizedOrder.Request;
            if (!normalizedRequest.Quantity.HasValue)
            {
                return new PlaceSpotOrderResult
                {
                    Success = false,
                    Error = "Order quantity is missing after normalization."
                };
            }

            var resolvedTradingMode = request.TradingMode ?? TradingMode.Spot;
            var executionIntent = request.ExecutionIntent ?? (request.Side == OrderSide.BUY
                ? TradeExecutionIntent.OpenLong
                : TradeExecutionIntent.CloseLong);
            var resolvedRawSignal = request.RawSignal ?? (request.Side == OrderSide.BUY ? TradeSignal.Buy : TradeSignal.Sell);
            var resolvedRequiresReducedPositionSize = request.RequiresReducedPositionSize ?? false;

            var riskResult = await riskManagementService.CheckOrderAsync(
                request.Symbol,
                request.Side,
                normalizedRequest.Quantity.Value,
                normalizedOrder.EffectivePrice ?? price,
                cancellationToken,
                requiresReducedPositionSize: resolvedRequiresReducedPositionSize,
                tradingMode: resolvedTradingMode,
                rawSignal: resolvedRawSignal,
                executionIntent: executionIntent);
            if (!riskResult.IsAllowed)
            {
                return new PlaceSpotOrderResult
                {
                    Success = false,
                    Error = riskResult.Reason
                };
            }
            var isSpotOpenLongEntry = resolvedTradingMode == TradingMode.Spot
                                      && executionIntent == TradeExecutionIntent.OpenLong
                                      && request.Side == OrderSide.BUY;
            if (isSpotOpenLongEntry)
            {
                var hasStrategyExpectedTarget = request.ExpectedTargetPrice.HasValue && request.ExpectedTargetPrice.Value > 0m;
                var requireStrategyExpectedTarget = configuration.GetValue<bool?>("Trading:RequireStrategyExpectedTargetForSpotOpenLong") ?? false;
                var isNormalTrendTarget = string.Equals(
                    request.ExpectedTargetSource,
                    "MovingAverageTrendStrategy.NormalTrendExpectedTarget",
                    StringComparison.Ordinal);
                var resolvedTargetPrice = hasStrategyExpectedTarget
                    ? request.ExpectedTargetPrice
                    : riskResult.TakeProfitPrice;
                var resolvedTargetSource = hasStrategyExpectedTarget
                    ? request.ExpectedTargetSource ?? "StrategyExpectedTarget"
                    : "RiskManagementService.TakeProfitPrice";
                var fallbackTargetUsed = !hasStrategyExpectedTarget;

                logger.LogInformation(
                    "Spot OpenLong expected target resolution: Symbol={Symbol}, ExpectedTargetPrice={ExpectedTargetPrice}, ExpectedMovePercent={ExpectedMovePercent}, ExpectedTargetSource={ExpectedTargetSource}, NormalTrendExpectedTargetPrice={NormalTrendExpectedTargetPrice}, NormalTrendExpectedMovePercent={NormalTrendExpectedMovePercent}, NormalTrendTargetSource={NormalTrendTargetSource}, BreakoutRangeHigh={BreakoutRangeHigh}, BreakoutRangeLow={BreakoutRangeLow}, BreakoutThresholdPrice={BreakoutThresholdPrice}, RiskTakeProfitPrice={RiskTakeProfitPrice}, ResolvedTargetPrice={ResolvedTargetPrice}, ResolvedTargetSource={ResolvedTargetSource}, RequireStrategyExpectedTargetForSpotOpenLong={RequireStrategyExpectedTargetForSpotOpenLong}, FallbackTargetUsed={FallbackTargetUsed}",
                    request.Symbol,
                    request.ExpectedTargetPrice,
                    request.ExpectedMovePercent,
                    request.ExpectedTargetSource,
                    isNormalTrendTarget ? request.ExpectedTargetPrice : null,
                    isNormalTrendTarget ? request.ExpectedMovePercent : null,
                    isNormalTrendTarget ? request.ExpectedTargetSource : null,
                    request.BreakoutRangeHigh,
                    request.BreakoutRangeLow,
                    request.BreakoutThresholdPrice,
                    riskResult.TakeProfitPrice,
                    resolvedTargetPrice,
                    resolvedTargetSource,
                    requireStrategyExpectedTarget,
                    fallbackTargetUsed);

                if (requireStrategyExpectedTarget && !hasStrategyExpectedTarget)
                {
                    logger.LogInformation(
                        "Spot OpenLong candidate rejected: Symbol={Symbol}, EntryPrice={EntryPrice}, ExpectedTargetPrice={ExpectedTargetPrice}, ExpectedMovePercent={ExpectedMovePercent}, ExpectedTargetSource={ExpectedTargetSource}, RecentSwingHigh={RecentSwingHigh}, RecentSwingLow={RecentSwingLow}, RangeOrAtrExtensionUsed={RangeOrAtrExtensionUsed}, AtrUsed={AtrUsed}, MinExpectedMovePercent={MinExpectedMovePercent}, MinNetProfitPercent={MinNetProfitPercent}, FeeRatePercent={FeeRatePercent}, SpreadPercent={SpreadPercent}, RejectionReason={RejectionReason}",
                        request.Symbol,
                        normalizedOrder.EffectivePrice ?? price,
                        request.ExpectedTargetPrice,
                        request.ExpectedMovePercent,
                        request.ExpectedTargetSource,
                        request.BreakoutRangeHigh,
                        request.BreakoutRangeLow,
                        request.ExpectedTargetStructureExtensionUsed,
                        request.ExpectedTargetAtrUsed,
                        configuration.GetValue<decimal?>("Trading:MinExpectedMovePercent") ?? 0.3m,
                        configuration.GetValue<decimal?>("Trading:MinNetProfitPercent") ?? 0.15m,
                        configuration.GetValue<decimal?>("Trading:FeeRatePercent") ?? 0.1m,
                        configuration.GetValue<decimal?>("Trading:EstimatedSpreadPercent") ?? 0.05m,
                        "Spot OpenLong blocked: strategy expected target is required and missing.");
                    return new PlaceSpotOrderResult
                    {
                        Success = false,
                        Error = "Spot OpenLong blocked: strategy expected target is required and missing."
                    };
                }

                var feeGuardResult = await feeProfitGuard.EvaluateAsync(
                    new FeeProfitGuardRequest
                    {
                        Symbol = request.Symbol,
                        TradingMode = resolvedTradingMode,
                        RawSignal = resolvedRawSignal,
                        ExecutionIntent = executionIntent,
                        Side = request.Side,
                        Quantity = normalizedRequest.Quantity.Value,
                        EntryPrice = normalizedOrder.EffectivePrice ?? price,
                        TargetPrice = resolvedTargetPrice,
                        TargetSource = resolvedTargetSource,
                        ExpectedMovePercent = request.ExpectedMovePercent,
                        RecentSwingHigh = request.BreakoutRangeHigh,
                        RecentSwingLow = request.BreakoutRangeLow,
                        RangeOrAtrExtensionUsed = request.ExpectedTargetStructureExtensionUsed,
                        AtrUsed = request.ExpectedTargetAtrUsed,
                        StopLossPrice = riskResult.StopLossPrice,
                        Caller = ResolveCaller(request.OrderSource),
                        IsProtectiveExit = false
                    },
                    cancellationToken);
                if (!feeGuardResult.IsAllowed)
                {
                    logger.LogInformation(
                        "Spot OpenLong candidate rejected: Symbol={Symbol}, EntryPrice={EntryPrice}, ExpectedTargetPrice={ExpectedTargetPrice}, ExpectedMovePercent={ExpectedMovePercent}, ExpectedTargetSource={ExpectedTargetSource}, RecentSwingHigh={RecentSwingHigh}, RecentSwingLow={RecentSwingLow}, RangeOrAtrExtensionUsed={RangeOrAtrExtensionUsed}, AtrUsed={AtrUsed}, MinExpectedMovePercent={MinExpectedMovePercent}, MinNetProfitPercent={MinNetProfitPercent}, FeeRatePercent={FeeRatePercent}, SpreadPercent={SpreadPercent}, GrossExpectedMovePercent={GrossExpectedMovePercent}, ExpectedNetProfitPercent={ExpectedNetProfitPercent}, RejectionReason={RejectionReason}",
                        request.Symbol,
                        feeGuardResult.EntryPrice,
                        request.ExpectedTargetPrice ?? resolvedTargetPrice,
                        request.ExpectedMovePercent,
                        request.ExpectedTargetSource ?? resolvedTargetSource,
                        request.BreakoutRangeHigh,
                        request.BreakoutRangeLow,
                        request.ExpectedTargetStructureExtensionUsed,
                        request.ExpectedTargetAtrUsed,
                        configuration.GetValue<decimal?>("Trading:MinExpectedMovePercent") ?? 0.3m,
                        configuration.GetValue<decimal?>("Trading:MinNetProfitPercent") ?? 0.15m,
                        feeGuardResult.EstimatedEntryFeePercent,
                        feeGuardResult.EstimatedSpreadPercent,
                        feeGuardResult.GrossExpectedProfitPercent,
                        feeGuardResult.NetExpectedProfitPercent,
                        feeGuardResult.Reason);
                    return new PlaceSpotOrderResult
                    {
                        Success = false,
                        Error = feeGuardResult.Reason
                    };
                }
            }

            var finalQueryParameters = BinanceRequestQueryBuilder.BuildRequestDictionary(normalizedRequest);
            finalQueryParameters["timestamp"] = normalizedRequest.Timestamp.ToString(CultureInfo.InvariantCulture);
            var finalQueryString = BinanceRequestQueryBuilder.BuildQueryString(finalQueryParameters);

            logger.LogInformation(
                "Sending Binance order: Symbol={Symbol}, Side={Side}, Type={Type}, OrderSource={OrderSource}, CloseReason={CloseReason}, ParentPositionId={ParentPositionId}, CorrelationId={CorrelationId}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, CandidatePrice={CandidatePrice}, OriginalQuantity={OriginalQuantity}, NormalizedQuantity={NormalizedQuantity}, FormattedQuantity={FormattedQuantity}, OriginalPrice={OriginalPrice}, NormalizedPrice={NormalizedPrice}, FormattedPrice={FormattedPrice}, StepSize={StepSize}, TickSize={TickSize}, MinQty={MinQty}, MaxQty={MaxQty}, MinNotional={MinNotional}, EffectivePrice={EffectivePrice}, Notional={Notional}, FinalQueryParams={FinalQueryParams}",
                normalizedRequest.Symbol,
                normalizedRequest.Side,
                normalizedRequest.Type,
                request.OrderSource,
                request.CloseReason,
                resolvedParentPositionId,
                request.CorrelationId,
                resolvedTradingMode,
                executionIntent,
                request.CandidatePrice,
                normalizedOrder.OriginalQuantity,
                normalizedOrder.NormalizedQuantity,
                normalizedOrder.NormalizedQuantity.HasValue ? BinanceDecimalFormatter.FormatQuantity(normalizedOrder.NormalizedQuantity.Value) : null,
                normalizedOrder.OriginalPrice,
                normalizedOrder.NormalizedPrice,
                normalizedOrder.NormalizedPrice.HasValue ? BinanceDecimalFormatter.FormatPrice(normalizedOrder.NormalizedPrice.Value) : null,
                normalizedOrder.Filters.StepSize,
                normalizedOrder.Filters.TickSize,
                normalizedOrder.Filters.MinQty,
                normalizedOrder.Filters.MaxQty,
                normalizedOrder.Filters.MinNotional,
                normalizedOrder.EffectivePrice,
                normalizedOrder.Notional,
                finalQueryString);
            var exchangeOrder = await toolService.BinanceClientService.Call<Domain.Models.TradingEndpoints.OrderResponse, NewOrderRequest>(
                normalizedRequest, newOrderEndpoint, true);
            var executedQty = exchangeOrder.ExecutedQty.ToDecimal();
            var order = new Order
            {
                ExchangeOrderId = exchangeOrder.OrderId,
                CorrelationId = request.CorrelationId,
                ParentPositionId = resolvedParentPositionId,
                OrderSource = request.OrderSource,
                CloseReason = request.CloseReason,
                Symbol = request.Symbol,
                Side = request.Side,
                Status = exchangeOrder.Status.ToOrderStatus(),
                ProcessingStatus = ProcessingStatus.OrderPlaced,
                Price = request.IsLimitOrder ? price : decimal.Parse(exchangeOrder.Price ?? "0", CultureInfo.InvariantCulture),
                Quantity = executedQty > 0 ? executedQty : normalizedRequest.Quantity.Value
            };

            await orderRepository.InsertAsync(order, cancellationToken);

            if (order.Status is OrderStatuses.FILLED or OrderStatuses.PARTIALLY_FILLED)
                await orderStatusService.TryUpdateProcessingStatusAsync(order.Id, ProcessingStatus.OrderPlaced, ProcessingStatus.TradesSyncPending, cancellationToken);

            if (exchangeOrder.Fills is { Count: > 0 })
            {
                var responseSymbol = Enum.TryParse<TradingSymbol>(exchangeOrder.Symbol, true, out var sym) ? sym : request.Symbol;
                var responseSide = Enum.TryParse<OrderSide>(exchangeOrder.Side, true, out var sid) ? sid : request.Side;
                var executedAt = DateTimeOffset.FromUnixTimeMilliseconds(exchangeOrder.TransactTime).UtcDateTime;

                foreach (var fill in exchangeOrder.Fills)
                {
                    var execution = new TradeExecution
                    {
                        OrderId = order.Id,
                        ExchangeOrderId = exchangeOrder.OrderId,
                        ExchangeTradeId = fill.TradeId,
                        Symbol = responseSymbol,
                        Side = responseSide,
                        Price = fill.Price.ToDecimal(),
                        Quantity = fill.Qty.ToDecimal(),
                        QuoteQuantity = fill.Price.ToDecimal() * fill.Qty.ToDecimal(),
                        Fee = fill.Commission.ToDecimal(),
                        FeeAsset = fill.CommissionAsset,
                        ExecutedAt = executedAt
                    };
                    await tradeExecutionRepository.InsertAsync(execution, cancellationToken);
                }

                var candidatePrice = request.CandidatePrice;
                var fillVwap = CalculateFillVwap(exchangeOrder.Fills);
                if (candidatePrice.HasValue && candidatePrice.Value > 0m && fillVwap.HasValue && fillVwap.Value > 0m)
                {
                    var slippagePercent = CalculateSlippagePercent(request.Side, candidatePrice.Value, fillVwap.Value);
                    logger.LogInformation(
                        "Post-fill slippage: Symbol={Symbol}, Side={Side}, LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}, CandidatePrice={CandidatePrice}, FillVwapPrice={FillVwapPrice}, SlippagePercent={SlippagePercent}",
                        request.Symbol,
                        request.Side,
                        order.Id,
                        order.ExchangeOrderId,
                        candidatePrice.Value,
                        fillVwap.Value,
                        slippagePercent);
                }
            }

            await tradeCooldownService.MarkTradeExecutedAsync(request.Symbol, cancellationToken);
            logger.LogInformation(
                "PlaceSpotOrderCommandHandler order persisted: OrderSource={OrderSource}, CloseReason={CloseReason}, ParentPositionId={ParentPositionId}, CorrelationId={CorrelationId}, Symbol={Symbol}, Side={Side}, Quantity={Quantity}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}",
                request.OrderSource,
                request.CloseReason,
                resolvedParentPositionId,
                request.CorrelationId,
                request.Symbol,
                request.Side,
                order.Quantity,
                resolvedTradingMode,
                executionIntent,
                order.Id,
                order.ExchangeOrderId);

            return new PlaceSpotOrderResult
            {
                Success = true,
                Order = order
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception in {handler} at {time}",
                nameof(PlaceSpotOrderCommandHandler),
                DateTime.UtcNow);

            return new PlaceSpotOrderResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    

    private async Task<decimal> GetCurrentPrice(TradingSymbol symbol, CancellationToken cancellationToken)
    {
        var endpoint = toolService.BinanceEndpointsService.GetEndpoint(TradingBot.Domain.Enums.Endpoints.MarketData.SymbolPriceTicker);
        var response = await toolService.BinanceClientService.Call<Domain.Models.MarketData.SymbolPriceTickerResponse, Domain.Models.MarketData.SymbolPriceTickerRequest>(
            new Domain.Models.MarketData.SymbolPriceTickerRequest
            {
                Symbol = symbol.ToString()
            }, endpoint, false);

        return decimal.Parse(response.Price, CultureInfo.InvariantCulture);
    }

    private static decimal? CalculateFillVwap(IReadOnlyCollection<Fill> fills)
    {
        if (fills.Count == 0)
            return null;

        decimal quoteSum = 0m;
        decimal quantitySum = 0m;
        foreach (var fill in fills)
        {
            var price = fill.Price.ToDecimal();
            var qty = fill.Qty.ToDecimal();
            if (price <= 0m || qty <= 0m)
                continue;

            quoteSum += price * qty;
            quantitySum += qty;
        }

        return quantitySum > 0m ? quoteSum / quantitySum : null;
    }

    private static decimal CalculateSlippagePercent(OrderSide side, decimal candidatePrice, decimal fillVwap)
    {
        if (candidatePrice <= 0m)
            return 0m;

        return side == OrderSide.BUY
            ? ((fillVwap - candidatePrice) / candidatePrice) * 100m
            : ((candidatePrice - fillVwap) / candidatePrice) * 100m;
    }

    private static string ResolveCaller(OrderSource source)
    {
        return source switch
        {
            OrderSource.DecisionWorker => nameof(DecisionWorker),
            OrderSource.TradeMonitorWorker => nameof(TradeMonitorWorker),
            OrderSource.Api => "ManualApi",
            OrderSource.Manual => "ManualApi",
            OrderSource.PositionReconciliationWorker => nameof(PositionReconciliationWorker),
            _ => nameof(PlaceSpotOrderCommandHandler)
        };
    }
}

