using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Domain.Models.AI;

public class BuySellRequest
{
    public string Symbol { get; set; }
    public decimal LastPrice { get; set; }
    public decimal _24hChange { get; set; }
    public decimal _24hVolume { get; set; }
    public OrderBook OrderBook { get; set; }
    public string Trend { get; set; }
    public List<RecentTrades> RecentTrades { get; set; }
    public List<Klines> Klines { get; set; }
}
