namespace TradingBot.Domain.Models.Diagnostics;

public sealed class TradingRuntimeHealthMetrics
{
    public bool HasPositionsIsClosingColumn { get; set; }
    public bool HasBalanceSnapshotHistoryTable { get; set; }

    public int ClosedPositionsWithNonZeroQuantity { get; set; }
    public int ClosedPositionsWithIsClosingTrue { get; set; }
    public int OpenPositionsWithNonPositiveQuantity { get; set; }
    public int OpenPositionsWithMissingAveragePrice { get; set; }
    public int ClosedPositionsMissingClosedAt { get; set; }
    public int ClosedPositionsMissingExitPrice { get; set; }

    public int PositionUpdatedOrdersWithUnprocessedExecutions { get; set; }
    public int TradeSyncedOrdersWithExecutions { get; set; }
    public int PositionUpdatingOrdersStuck { get; set; }
    public int FilledOrdersWithoutExecutions { get; set; }
    public int TradeExecutionsWithoutMatchingOrders { get; set; }

    public int ParentPositionsWithMultipleActiveCloseOrders { get; set; }
    public int ClosingPositionsWithoutActiveCloseOrder { get; set; }
    public int OpenPositionsWithActiveCloseOrderButNotClosing { get; set; }

    public DateTime? LatestBnbAt { get; set; }
    public DateTime? LatestUsdtAt { get; set; }
    public bool BalanceSnapshotHistoryEmpty { get; set; }
}
