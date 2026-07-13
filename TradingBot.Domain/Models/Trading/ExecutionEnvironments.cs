namespace TradingBot.Domain.Models.Trading;

/// <summary>
/// Canonical values for <see cref="Order.ExecutionEnvironment"/> and
/// <see cref="Position.ExecutionEnvironment"/>. Null means live Binance Spot (legacy).
/// </summary>
public static class ExecutionEnvironments
{
    /// <summary>ETH15 fixed-frequency forward-incubation validation on Binance Futures Testnet.</summary>
    public const string BinanceFuturesTestnet = "BinanceFuturesTestnet";

    /// <summary>
    /// SpotFuturesCrossMarketTestnetV1 runtime strategy on Binance USD-M Futures Testnet.
    /// Value kept &lt;= 24 chars to fit the execution_environment varchar(24) columns.
    /// </summary>
    public const string SpotFuturesCrossMarketTestnetV1 = "SpotFuturesXTestnetV1";
}
