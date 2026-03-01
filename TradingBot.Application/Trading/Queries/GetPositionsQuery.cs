using MediatR;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.Trading.Queries;

public record GetPositionsQuery : IRequest<IReadOnlyList<Position>>;

