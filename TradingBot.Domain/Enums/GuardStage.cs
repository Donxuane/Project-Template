namespace TradingBot.Domain.Enums;

public enum GuardStage
{
    None = 0,
    Cooldown = 1,
    PositionGuard = 3,
    Risk = 4,
    Execution = 7
}
