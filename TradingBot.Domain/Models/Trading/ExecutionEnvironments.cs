namespace TradingBot.Domain.Models.Trading;

/// <summary>
/// Canonical execution environment for the retained Spot/Futures feature.
/// </summary>
public static class ExecutionEnvironments
{
    /// <summary>Value remains within the database column's 24-character limit.</summary>
    public const string SpotFuturesCrossMarketTestnetV3 = "SpotFuturesXTestnetV3";
}
