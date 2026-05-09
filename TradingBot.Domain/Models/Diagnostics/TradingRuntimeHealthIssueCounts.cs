namespace TradingBot.Domain.Models.Diagnostics;

public sealed class TradingRuntimeHealthIssueCounts
{
    public int Critical { get; set; }
    public int Warnings { get; set; }
    public int SchemaCritical { get; set; }
    public int PositionCritical { get; set; }
    public int LifecycleCritical { get; set; }
    public int CloseSafetyCritical { get; set; }
    public int CloseSafetyWarnings { get; set; }
    public int BalanceWarnings { get; set; }
}
