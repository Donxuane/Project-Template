using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Percistance.Services;

public class SlicerService : ISlicerService
{
    public decimal GetSliceAmount(SymbolPriceTickerResponse ticker, decimal portion)
    {
        var asset = new { Amount = 1, ticker.Price };
        var slice = portion * asset.Amount / decimal.Parse(asset.Price);
        return slice;
    }
}
