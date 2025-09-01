using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Domain.Interfaces.Services;

public interface ISlicerService
{
    public decimal GetSliceAmount(SymbolPriceTickerResponse ticker, decimal portion);
}
