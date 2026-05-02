using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Services;

public interface IPositionAccountingService
{
    PositionAccountingResult ApplyTrades(
        Position? currentPosition,
        Order order,
        IReadOnlyList<TradeExecution> trades);
}
