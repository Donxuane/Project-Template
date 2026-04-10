using MediatR;
using TradingBot.Application.Trading.Queries;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.Trading.QueriesHandlers;

public class GetOpenOrdersQueryHandler(IOrderRepository orderRepository)
    : IRequestHandler<GetOpenOrdersQuery, IReadOnlyList<Order>>
{
    public async Task<IReadOnlyList<Order>> Handle(GetOpenOrdersQuery request, CancellationToken cancellationToken)
    {
        return await orderRepository.GetOpenOrdersAsync(request.Symbol, null, cancellationToken);
    }
}

