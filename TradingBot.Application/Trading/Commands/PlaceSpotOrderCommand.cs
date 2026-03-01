using MediatR;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.Trading.Commands;

public record PlaceSpotOrderCommand(
    TradingSymbol Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal? Price,
    bool IsLimitOrder
) : IRequest<PlaceSpotOrderResult>;

public sealed class PlaceSpotOrderResult
{
    public Order? Order { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
}

