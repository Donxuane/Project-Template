namespace TradingBot.Domain.Models.Trading;

/// <summary>
/// Canonical values for <see cref="Order.ExecutionEnvironment"/> and
/// <see cref="Position.ExecutionEnvironment"/>. Null means live Binance Spot (legacy).
/// </summary>
public static class ExecutionEnvironments
{
    /// <summary>ETH15 fixed-frequency forward-incubation validation on Binance Futures Testnet.</summary>
    public const string BinanceFuturesTestnet = "BinanceFuturesTestnet";
}
