using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesValidationV31Simulator
{
    public static DirectionalRuleV31ScanResult ScanProfile(
        DirectionalRuleV31SimulationProfile profile,
        string windowLabel,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> sourceOneMinuteCandles,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var v2 = DirectionalRuleFuturesValidationV31Catalog.ToV2Profile(profile);
        var scan = DirectionalRuleFuturesValidationV2Simulator.ScanProfile(
            v2, windowLabel, intervalCandles, sourceOneMinuteCandles, btcContext, marketWideContext, cancellationToken);
        var trades = scan.Trades.Select(t => MapBaseTrade(profile, t)).ToArray();
        return new DirectionalRuleV31ScanResult(trades, scan.Skipped, scan.SignalCount);
    }

    public static IReadOnlyList<DirectionalRuleV31TradeRecord> ApplyCostScenarios(
        IReadOnlyList<DirectionalRuleV31TradeRecord> baseTrades)
    {
        if (baseTrades.Count == 0)
            return [];

        var scenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios();
        var expanded = new List<DirectionalRuleV31TradeRecord>(baseTrades.Count * scenarios.Count);
        foreach (var trade in baseTrades)
        {
            var simulation = new DirectionalTradeSimulationResult(
                trade.EntryTimeUtc,
                trade.EntryPrice,
                trade.ExitTimeUtc,
                trade.ExitPrice,
                trade.ExitReason,
                trade.MfePercent,
                trade.MaePercent,
                trade.DurationMinutes);
            foreach (var scenario in scenarios)
            {
                var costs = DirectionalRuleFuturesValidationV3CostModel.ComputeCostBreakdown(
                    simulation, trade.Direction, scenario);
                expanded.Add(trade with
                {
                    CostScenarioLabel = scenario.Label,
                    NetPnlQuote = costs.NetPnlQuote,
                    FeesQuote = costs.FeeEstimateQuote,
                    SlippageQuote = costs.SlippageEstimateQuote + costs.SpreadEstimateQuote,
                    FundingQuote = costs.FundingEstimateQuote
                });
            }
        }

        return expanded;
    }

    private static DirectionalRuleV31TradeRecord MapBaseTrade(
        DirectionalRuleV31SimulationProfile profile,
        DirectionalRuleV2TradeRecord trade)
        => new()
        {
            ProfileKey = profile.ProfileKey,
            VariantLabel = profile.VariantLabel,
            ValidationTrack = profile.ValidationTrack,
            IsBestBnbCandidate = profile.IsBestBnbCandidate,
            RuleName = trade.RuleName,
            Direction = trade.Direction,
            Symbol = trade.Symbol,
            Interval = trade.Interval,
            WindowLabel = trade.WindowLabel,
            EntryMode = trade.EntryMode,
            TargetPercent = trade.TargetPercent,
            StopPercent = trade.StopPercent,
            MaxHoldMinutes = trade.MaxHoldMinutes,
            CooldownCandlesAfterExit = trade.CooldownCandlesAfterExit,
            OverlapPolicy = trade.OverlapPolicy,
            CostScenarioLabel = string.Empty,
            EntryTimeUtc = trade.TimeUtc,
            ExitTimeUtc = trade.TimeUtc.AddMinutes((double)trade.DurationMinutes),
            EntryPrice = trade.EntryPrice,
            ExitPrice = trade.ExitPrice,
            ExitReason = trade.ExitReason,
            GrossPnlQuote = trade.GrossPnlQuote,
            NetPnlQuote = trade.GrossPnlQuote,
            BtcReturn30mPercent = trade.BtcReturn30mPercent,
            VolatilityRegime = trade.VolatilityRegime,
            DistanceFromRecentHighPercent = trade.DistanceFromRecentHighPercent,
            AtrPercent = trade.AtrPercent,
            TrendSlopePercent = trade.TrendSlopePercent,
            MfePercent = trade.MfePercent,
            MaePercent = trade.MaePercent,
            DurationMinutes = trade.DurationMinutes
        };
}

public sealed record DirectionalRuleV31ScanResult(
    IReadOnlyList<DirectionalRuleV31TradeRecord> Trades,
    IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> Skipped,
    int SignalCount);
