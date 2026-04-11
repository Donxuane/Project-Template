using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;
using TradingBot.Domain.Models.TradingEndpoints;

namespace TradingBot.Application.Trading;

public class PlaceSpotOrderCommandHandler(
    IToolService toolService,
    IOrderRepository orderRepository,
    ITradeExecutionRepository tradeExecutionRepository,
    IOrderStatusService orderStatusService,
    IRiskManagementService riskManagementService,
    ITimeSyncService timeSyncService,
    IPriceCacheService priceCacheService,
    ILogger<PlaceSpotOrderCommandHandler> logger) : IRequestHandler<PlaceSpotOrderCommand, PlaceSpotOrderResult>
{
    public async Task<PlaceSpotOrderResult> Handle(PlaceSpotOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
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

            var riskResult = await riskManagementService.CheckOrderAsync(request.Symbol, request.Side, request.Quantity, price, cancellationToken);
            if (!riskResult.IsAllowed)
            {
                return new PlaceSpotOrderResult
                {
                    Success = false,
                    Error = riskResult.Reason
                };
            }

            var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync(cancellationToken);

            var newOrderEndpoint = toolService.BinanceEndpointsService.GetEndpoint(TradingBot.Domain.Enums.Endpoints.Trading.NewOrder);
            var side = request.Side == OrderSide.BUY ? OrderSide.BUY : OrderSide.SELL;

            var newOrderRequest = new NewOrderRequest
            {
                Symbol = request.Symbol.ToString(),
                Side = side,
                Type = request.IsLimitOrder ? OrderTypes.LIMIT : OrderTypes.MARKET,
                Quantity = request.Quantity,
                Price = request.IsLimitOrder ? price : 0m,
                TimeInForce = request.IsLimitOrder ? TimeInForce.GTC : null,
                Timestamp = adjustedTimestamp,
                RecvWindow = 30000
            };

            var exchangeOrder = await toolService.BinanceClientService.Call<Domain.Models.TradingEndpoints.OrderResponse, NewOrderRequest>(
                newOrderRequest, newOrderEndpoint, true);
            var executedQty = exchangeOrder.ExecutedQty.ToDecimal();
            var order = new Order
            {
                ExchangeOrderId = exchangeOrder.OrderId,
                Symbol = request.Symbol,
                Side = request.Side,
                Status = exchangeOrder.Status.ToOrderStatus(),
                ProcessingStatus = ProcessingStatus.OrderPlaced,
                Price = request.IsLimitOrder ? price : decimal.Parse(exchangeOrder.Price ?? "0", CultureInfo.InvariantCulture),
                Quantity = executedQty > 0 ? executedQty : request.Quantity
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
                        ExecutedAt = executedAt
                    };
                    await tradeExecutionRepository.InsertAsync(execution, cancellationToken);
                }
            }

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
}

