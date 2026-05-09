namespace TradingBot.Domain.Models.Trading;

public sealed class BalanceSnapshotHistory
{
    public long Id { get; set; }
    public string Asset { get; set; } = string.Empty;
    public int? AssetId { get; set; }
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal Total { get; set; }
    public string Source { get; set; } = "BinanceAccount";
    public string? SyncCorrelationId { get; set; }
    public DateTime CapturedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
