using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Services;

public interface IPositionReconciliationService
{
    IReadOnlyList<ReconciliationResult> EvaluateSpot(
        IReadOnlyList<Position> openPositions,
        IReadOnlyList<Position> closedPositions,
        IReadOnlyList<Balance> exchangeBalances,
        IReadOnlyDictionary<string, BalanceSnapshot> latestSnapshotsByAsset,
        decimal tolerance,
        int maxOpenPositionsPerSymbol,
        TimeSpan snapshotMaxAge);
}
