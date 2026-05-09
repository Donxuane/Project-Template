namespace TradingBot.Domain.Models.Trading;

public sealed class BalanceSyncWriteResult
{
    public int AssetsFetched { get; init; }
    public int LatestRowsUpserted { get; init; }
    public int HistoryRowsInserted { get; init; }
}
