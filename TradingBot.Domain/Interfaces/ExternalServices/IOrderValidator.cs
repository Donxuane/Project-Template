using TradingBot.Domain.Models;

namespace TradingBot.Domain.Interfaces.ExternalServices;

public interface IOrderValidator
{
    public (decimal price, decimal qty) ValidatedOrder(decimal price, decimal qty, OrderFilters filters);
}
