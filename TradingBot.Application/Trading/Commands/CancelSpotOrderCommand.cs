using MediatR;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.Trading.Commands;

public record CancelSpotOrderCommand(
    TradingSymbol Symbol,
    long ExchangeOrderId
) : IRequest<CancelSpotOrderResult>;

public sealed class CancelSpotOrderResult
{
    public Order? Order { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
}

