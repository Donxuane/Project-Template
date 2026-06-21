using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class NoPaidDataAdaptiveActivationV1Tests
{
    [Fact]
    public void Catalog_BuildsControlledActivationMatrix()
    {
        var rules = NoPaidDataAdaptiveActivationV1Catalog.BuildActivationRules(0.10m);
        Assert.Contains(rules, r => r.ConditionType == AdaptiveActivationConditionType.AlwaysOn);
        Assert.Contains(rules, r => r.ConditionType == AdaptiveActivationConditionType.RecentNetPositive);
        Assert.Contains(rules, r => r.ConditionType == AdaptiveActivationConditionType.DrawdownGuard);
        Assert.Contains(rules, r => r.ConditionType == AdaptiveActivationConditionType.RegimeRecentPerformance
                                      && r.RegimeFilter == AdaptiveRegimeFilterKind.Btc30Q3VolNormal);
        Assert.True(rules.Count > 100);
    }

    [Fact]
    public void Engine_UsesOnlyPastCompletedTradesAtCheckpoint()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var trades = new List<RegimeDriftDiagnosticTrade>();
        for (var i = 0; i < 20; i++)
        {
            var entry = start.AddDays(i);
            trades.Add(new RegimeDriftDiagnosticTrade
            {
                EntryTimeUtc = entry,
                ExitTimeUtc = entry.AddHours(4),
                NetPnlQuote = i < 10 ? 5m : -5m,
                IsWinner = i < 10,
                ExitReason = "Target",
                InOlder = true,
                InRecent90d = false,
                MonthKey = entry.ToString("yyyy-MM")
            });
        }

        var rule = new AdaptiveActivationRuleConfig(
            "Test_NetPos", AdaptiveActivationConditionType.RecentNetPositive,
            1, 14, 3, 5, null, null, null, 0, AdaptiveRegimeFilterKind.None, "test");
        var btcCandles = BuildFlatBtc(start, 40);
        var bnbCandles = BuildFlatBnb(start, 40);
        var btcContext = new BtcContextIndex(btcCandles);

        var sim = NoPaidDataAdaptiveActivationV1Engine.Simulate(
            rule, trades, trades, start, start.AddDays(40), btcContext, bnbCandles, 0m, "futures-moderate");

        var firstCheckpoint = sim.Periods.First(p => p.CheckpointUtc >= start.AddDays(14));
        Assert.True(firstCheckpoint.LookbackTradeCount <= 14);
        Assert.All(sim.Periods, p => Assert.True(p.CheckpointUtc < p.ActivationEndUtc || !p.Activated));
    }

    [Fact]
    public void Engine_AlwaysOn_MatchesBaselineTradeCount()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var trades = Enumerable.Range(0, 12).Select(i => new RegimeDriftDiagnosticTrade
        {
            EntryTimeUtc = start.AddDays(i),
            ExitTimeUtc = start.AddDays(i).AddHours(4),
            NetPnlQuote = 1m,
            IsWinner = true,
            ExitReason = "Target",
            InOlder = true,
            InRecent90d = false,
            MonthKey = "2025-01"
        }).ToArray();

        var rule = NoPaidDataAdaptiveActivationV1Catalog.BuildActivationRules(0m)
            .First(r => r.ConditionType == AdaptiveActivationConditionType.AlwaysOn);
        var sim = NoPaidDataAdaptiveActivationV1Engine.Simulate(
            rule, trades, trades, start, start.AddDays(30), new BtcContextIndex(BuildFlatBtc(start, 30)),
            BuildFlatBnb(start, 30), 0m, "futures-moderate");

        Assert.Equal(trades.Length, sim.Summary.TotalTrades);
        Assert.Equal(trades.Sum(t => t.NetPnlQuote), sim.Summary.Full365NetPnl);
    }

    private static List<KlineCandle> BuildFlatBtc(DateTime start, int days)
        => Enumerable.Range(0, days * 24 * 60)
            .Select(i => new KlineCandle(TradingSymbol.BTCUSDT, start.AddMinutes(i), 100m, 101m, 99m, 100m, 1m))
            .ToList();

    private static List<KlineCandle> BuildFlatBnb(DateTime start, int days)
        => Enumerable.Range(0, days * 288)
            .Select(i => new KlineCandle(TradingSymbol.BNBUSDT, start.AddMinutes(i * 5), 50m, 51m, 49m, 50m, 1m))
            .ToList();
}
