using Microsoft.Extensions.Configuration;

namespace TradingBot.Backtest;

/// <summary>
/// Safety configuration for Binance Futures testnet shadow execution preparation.
/// Defaults are shadow/dry-run only; real orders are never permitted.
/// </summary>
public sealed class FuturesTestnetShadowSettings
{
    public bool Enabled { get; init; }
    public bool DryRunOnly { get; init; } = true;
    public bool AllowTestnetOrders { get; init; }
    public bool AllowRealOrders { get; } = false;
    public decimal MaxNotionalUsdt { get; init; } = 25m;
    public int Leverage { get; init; } = 1;
    public bool RequireForwardTradeEvidence { get; init; } = true;
    public string? TestnetApiKey { get; init; }
    public string? TestnetSecretKey { get; init; }
    public string? TestnetBaseUrl { get; init; }

    public static FuturesTestnetShadowSettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("FuturesTestnetShadow");
        return new FuturesTestnetShadowSettings
        {
            Enabled = section.GetValue("Enabled", false),
            DryRunOnly = section.GetValue("DryRunOnly", true),
            AllowTestnetOrders = section.GetValue("AllowTestnetOrders", false),
            MaxNotionalUsdt = section.GetValue("MaxNotionalUSDT", 25m),
            Leverage = Math.Max(1, section.GetValue("Leverage", 1)),
            RequireForwardTradeEvidence = section.GetValue("RequireForwardTradeEvidence", true),
            TestnetApiKey = section["TestnetApiKey"],
            TestnetSecretKey = section["TestnetSecretKey"],
            TestnetBaseUrl = section["TestnetBaseUrl"]
        };
    }
}
