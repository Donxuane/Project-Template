namespace TradingBot.Domain.Enums;

public enum OrderSource
{
    Unknown = 0,
    DecisionWorker = 1,
    TradeMonitorWorker = 2,
    PositionReconciliationWorker = 3,
    Manual = 4,
    Api = 5
}
