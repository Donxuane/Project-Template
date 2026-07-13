namespace TradingBot.Domain.Enums;

public enum AdaptiveRollingProfitExitState
{
    Monitoring = 0,
    ProfitEligible = 1,
    ProfitArmed = 2,
    RidingTrend = 3,
    ExitPending = 4,
    Closing = 5,
    Closed = 6,
    DisabledOrDegraded = 7
}
