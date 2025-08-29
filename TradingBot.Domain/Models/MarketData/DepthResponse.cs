namespace TradingBot.Domain.Models.MarketData;

public class DepthResponse
{
    public long LastUpdateId { get; set; }
    public List<OrderBookEntry> Bids { get; set; }
    public List<OrderBookEntry> Asks { get; set; }
}