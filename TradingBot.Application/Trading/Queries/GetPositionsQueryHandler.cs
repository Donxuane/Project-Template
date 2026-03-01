using MediatR;
using TradingBot.Application.Trading.Queries;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.Trading.QueriesHandlers;

public class GetPositionsQueryHandler(IPositionRepository positionRepository)
    : IRequestHandler<GetPositionsQuery, IReadOnlyList<Position>>
{
    public async Task<IReadOnlyList<Position>> Handle(GetPositionsQuery request, CancellationToken cancellationToken)
    {
        return await positionRepository.GetOpenPositionsAsync(cancellationToken);
    }
}

