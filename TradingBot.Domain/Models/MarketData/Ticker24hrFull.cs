namespace TradingBot.Domain.Models.MarketData;

public class Ticker24hrFull
{
    public string Symbol { get; set; }
    public decimal PriceChange { get; set; }
    public decimal PriceChangePercent { get; set; }
    public decimal WeightedAvgPrice { get; set; }
    public decimal PrevClosePrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal LastQty { get; set; }
    public decimal BidPrice { get; set; }
    public decimal BidQty { get; set; }
    public decimal AskPrice { get; set; }
    public decimal AskQty { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal Volume { get; set; }
    public decimal QuoteVolume { get; set; }
    public long OpenTime { get; set; }
    public long CloseTime { get; set; }
    public long FirstId { get; set; }
    public long LastId { get; set; }
    public int Count { get; set; }
}
