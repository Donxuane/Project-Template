using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog
{
    public const string VariantLabel = "BestBnb_NextClose_4h_cd6_1.75_1.00";
    public const string PrimaryCostScenario = "futures-moderate";
    public const int MinimumMeaningfulTrades = 50;
    public const int MinimumRuleTrainSamples = 50;
    public const int MinimumRuleTestSamples = 15;

    public static readonly string[] StressCostScenarios =
    [
        "futures-stress",
        "futures-stress-plus",
        "futures-moderate-latency-005",
        "futures-stress-latency-005"
    ];

    public static DirectionalRuleV31SimulationProfile BuildProfile(DirectionalRuleDefinition rule)
        => new(
            DirectionalRuleFuturesValidationV31Catalog.BuildProfileKey(
                TradingSymbol.BNBUSDT, "5m", DirectionalRuleEntryMode.NextClose,
                DirectionalRuleFuturesValidationV31Catalog.OverlapPolicy, 6, 240, 1.75m, 1.00m),
            VariantLabel,
            DirectionalRuleV31ValidationTrack.BestBnbLongHistory,
            true,
            rule,
            TradingSymbol.BNBUSDT,
            "5m",
            1.75m,
            1.00m,
            240,
            DirectionalRuleEntryMode.NextClose,
            DirectionalRuleFuturesValidationV31Catalog.OverlapPolicy,
            6);

    public static RegimeDriftDiagnosticTrade MapTrade(
        DirectionalRuleV2TradeRecord trade,
        BtcContextIndex? btcContext,
        DateTime dataEndUtc)
    {
        var holdoutStart = dataEndUtc.AddDays(-30);
        var recent90Start = dataEndUtc.AddDays(-90);
        var recent60Start = dataEndUtc.AddDays(-60);
        var recent30Start = dataEndUtc.AddDays(-30);
        var entry = trade.TimeUtc;
        var btc = btcContext?.GetSnapshot(entry);

        return new RegimeDriftDiagnosticTrade
        {
            EntryTimeUtc = entry,
            ExitTimeUtc = entry.AddMinutes((double)trade.DurationMinutes),
            NetPnlQuote = trade.NetPnlQuote,
            GrossPnlQuote = trade.GrossPnlQuote,
            IsWinner = trade.NetPnlQuote > 0m,
            CostScenarioLabel = trade.CostScenarioLabel,
            ExitReason = trade.ExitReason,
            MfePercent = trade.MfePercent,
            MaePercent = trade.MaePercent,
            DistanceFromRecentHighPercent = trade.DistanceFromRecentHighPercent,
            DistanceFromRecentLowPercent = trade.DistanceFromRecentLowPercent,
            RangeWidthPercent = trade.RangeWidthPercent,
            AtrPercent = trade.AtrPercent,
            TrendSlopePercent = trade.TrendSlopePercent,
            BtcReturn30mPercent = trade.BtcReturn30mPercent ?? btc?.BtcReturn30mPercent,
            BtcReturn60mPercent = btc?.BtcReturn60mPercent,
            VolatilityRegime = trade.VolatilityRegime,
            BtcTrendRegime = btc?.BtcTrendRegime,
            BtcVolatilityRegime = btc?.BtcVolatilityRegime,
            BtcMarketDirectionBucket = btc?.BtcMarketDirectionBucket,
            HourOfDayUtc = entry.Hour,
            DayOfWeek = entry.DayOfWeek.ToString(),
            SessionBucket = MarketRegimeForwardEdgeScanner.ResolveSessionBucket(entry.Hour),
            MonthKey = $"{entry.Year}-{entry.Month:D2}",
            InRecent30d = entry >= recent30Start,
            InRecent60d = entry >= recent60Start,
            InRecent90d = entry >= recent90Start,
            InOlder = entry < recent90Start,
            InTrainReference = entry < holdoutStart,
            InHoldout30d = entry >= holdoutStart
        };
    }

    public static IReadOnlyList<DirectionalRuleV2TradeRecord> ApplyCostScenario(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        string scenarioLabel)
    {
        var scenario = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios()
            .FirstOrDefault(s => string.Equals(s.Label, scenarioLabel, StringComparison.OrdinalIgnoreCase));
        if (scenario is null)
            return [];

        return baseTrades.Select(trade =>
        {
            var simulation = new DirectionalTradeSimulationResult(
                trade.TimeUtc,
                trade.EntryPrice,
                trade.TimeUtc.AddMinutes((double)trade.DurationMinutes),
                trade.ExitPrice,
                trade.ExitReason,
                trade.MfePercent,
                trade.MaePercent,
                trade.DurationMinutes);
            var costs = DirectionalRuleFuturesValidationV3CostModel.ComputeCostBreakdown(
                simulation, trade.Direction, scenario);
            return trade with
            {
                CostScenarioLabel = scenario.Label,
                NetPnlQuote = costs.NetPnlQuote
            };
        }).ToArray();
    }
}
