using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface IOrderRepository
{
    Task<long> InsertAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task<bool> HasActiveCloseOrderForPositionByEnvironmentAsync(
        long parentPositionId,
        string executionEnvironment,
        CancellationToken cancellationToken = default);
}
