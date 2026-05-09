namespace TradingBot.Domain.Enums;

public enum PositionExitReason
{
    None = 0,
    StopLoss = 1,
    TakeProfit = 2,
    Time = 3,
    TrailingStop = 4,
    ManualClose = 5,
    OppositeSignal = 6,
    Reconciliation = 7,
    RiskExit = 8,
    Unknown = 99
}
