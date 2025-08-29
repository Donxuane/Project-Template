namespace TradingBot.Domain.Models.MarketData;

public class AggregateTrade
{
    public long A { get; set; }  // aggregate tradeId
    public decimal P { get; set; } // price
    public decimal Q { get; set; } // quantity
    public long F { get; set; } // first tradeId
    public long L { get; set; } // last tradeId
    public long T { get; set; } // timestamp
    public bool M { get; set; } // isBuyerMaker
    public bool BestMatch { get; set; } // maps from "M"
}
