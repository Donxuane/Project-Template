namespace TradingBot.Domain.Models.AccountInformation;

public class PreventedMatchResponse
{
    public string Symbol { get; set; } = string.Empty;
    public long PreventedMatchId { get; set; }
    public long TakerOrderId { get; set; }
    public string MakerSymbol { get; set; } = string.Empty;
    public long MakerOrderId { get; set; }
    public long TradeGroupId { get; set; }
    public string SelfTradePreventionMode { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public string MakerPreventedQuantity { get; set; } = string.Empty;
    public long TransactTime { get; set; }
}
