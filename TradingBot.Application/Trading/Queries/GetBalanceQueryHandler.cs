using MediatR;
using TradingBot.Application.Trading.Queries;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.Trading.QueriesHandlers;

public class GetBalanceQueryHandler(IBalanceRepository balanceRepository)
    : IRequestHandler<GetBalanceQuery, IReadOnlyList<BalanceSnapshot>>
{
    public async Task<IReadOnlyList<BalanceSnapshot>> Handle(GetBalanceQuery request, CancellationToken cancellationToken)
    {
        return await balanceRepository.GetLatestForAllAsync(cancellationToken);
    }
}

