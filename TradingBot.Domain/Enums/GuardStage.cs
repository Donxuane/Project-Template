namespace TradingBot.Domain.Enums;

public enum GuardStage
{
    None = 0,
    Cooldown = 1,
    Idempotency = 2,
    PositionGuard = 3,
    Risk = 4,
    FeeProfitGuard = 5,
    ConfidenceGate = 6,
    Execution = 7,
    UnsupportedMode = 8
}
