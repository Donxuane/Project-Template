namespace TradingBot.Domain.Enums;

public enum CloseReason
{
    None = 0,
    StopLoss = 1,
    TakeProfit = 2,
    MaxDuration = 3,
    ManualClose = 4,
    Reconciliation = 5,
    OppositeSignal = 6,
    RiskExit = 7,
    Unknown = 99
}
