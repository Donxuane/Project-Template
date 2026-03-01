using MediatR;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.Trading.Queries;

public record GetOpenOrdersQuery(TradingSymbol? Symbol) : IRequest<IReadOnlyList<Order>>;

