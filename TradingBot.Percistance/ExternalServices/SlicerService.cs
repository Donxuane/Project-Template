using TradingBot.Domain.Interfaces.ExternalServices;
using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Percistance.ExternalServices;

public class SlicerService : ISlicerService
{
    public decimal GetSliceAmount(SymbolPriceTickerResponse ticker, decimal portion)
    {
        var asset = new { Amount = 1, Price = ticker.Price };
        var slice = (portion * asset.Amount) / decimal.Parse(asset.Price);
        return slice;
    }
}
