using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Domain.Interfaces.ExternalServices;

public interface ISlicerService
{
    public decimal GetSliceAmount(SymbolPriceTickerResponse ticker, decimal portion);
}
