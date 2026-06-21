using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class DirectionalRuleFuturesValidationV31Tests
{
    [Fact]
    public void BuildCrossSymbolProfiles_GeneratesSmallMatrixPerSymbol()
    {
        var rules = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules();
        var rule = rules.First(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase));
        var profiles = DirectionalRuleFuturesValidationV31Catalog.BuildCrossSymbolProfiles(
            rule, [TradingSymbol.BNBUSDT, TradingSymbol.ETHUSDT]);
        Assert.Equal(48, profiles.Count);
        Assert.All(profiles, p =>
        {
            Assert.Equal(DirectionalRuleV31ValidationTrack.CrossSymbol, p.ValidationTrack);
            Assert.Equal(DirectionalRuleEntryMode.NextClose, p.EntryMode);
            Assert.False(p.IsBestBnbCandidate);
        });
    }

    [Fact]
    public void BuildBestBnbLongHistoryProfile_MatchesBestCandidate()
    {
        var rules = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules();
        var rule = rules.First(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase));
        var profile = DirectionalRuleFuturesValidationV31Catalog.BuildBestBnbLongHistoryProfile(rule);
        Assert.True(profile.IsBestBnbCandidate);
        Assert.Equal(TradingSymbol.BNBUSDT, profile.Symbol);
        Assert.Equal("5m", profile.Interval);
        Assert.Equal(240, profile.MaxHoldMinutes);
        Assert.Equal(6, profile.CooldownCandlesAfterExit);
        Assert.Equal(1.75m, profile.TargetPercent);
        Assert.Equal(1.00m, profile.StopPercent);
    }

    [Fact]
    public void FilterWindows_RemovesUnavailableSpans()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var filtered = DirectionalRuleFuturesValidationV31Application.FilterWindows([30, 60, 90, 180, 270, 365], start, end);
        Assert.Contains(180, filtered);
        Assert.DoesNotContain(365, filtered);
    }

    [Fact]
    public void BuildWindowRobustness_SetsGeneralizationLabels()
    {
        var summaries = new[]
        {
            Summary("p1", TradingSymbol.BNBUSDT, "180d", "futures-moderate", 60m, 55),
            Summary("p1", TradingSymbol.BNBUSDT, "holdout30d", "futures-moderate", 20m, 20),
            Summary("p1", TradingSymbol.BNBUSDT, "trainReference", "futures-moderate", 15m, 15)
        };
        var robust = DirectionalRuleFuturesValidationV31Aggregator.BuildWindowRobustness(summaries).Single();
        Assert.True(robust.AllWindowsPositive);
        Assert.True(robust.TradeCountSufficient);
        Assert.True(robust.LongHistoryPositive);
    }

    private static DirectionalRuleV31SummaryRow Summary(
        string profileKey,
        TradingSymbol symbol,
        string window,
        string cost,
        decimal net,
        int trades)
        => new()
        {
            ProfileKey = profileKey,
            VariantLabel = "test",
            ValidationTrack = DirectionalRuleV31ValidationTrack.BestBnbLongHistory,
            IsBestBnbCandidate = true,
            Symbol = symbol,
            Interval = "5m",
            WindowLabel = window,
            CostScenarioLabel = cost,
            NetPnlQuote = net,
            ExecutedTrades = trades,
            EntryMode = "NextClose",
            OverlapPolicy = "OneOpenTradePerRuleSymbol",
            CooldownCandlesAfterExit = 6,
            TargetPercent = 1.75m,
            StopPercent = 1.00m,
            MaxHoldMinutes = 240
        };
}
