namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesRegimeConditionalV2Catalog
{
    public const string PrimaryCostScenario = "futures-moderate";
    public const int MinimumTotalTrades = 50;
    public const int MinimumPeriodTrades = 15;

    // Near-breakeven tolerance: a period counts as viable if its average net per trade
    // is at least this value (i.e. losing no more than 0.05 quote per trade on average).
    public const decimal NearBreakevenAvgPerTrade = -0.05m;

    public static readonly string[] CostStressScenarios =
    [
        "futures-low",
        "futures-moderate",
        "futures-stress",
        "futures-stress-plus",
        "futures-moderate-latency-002",
        "futures-moderate-latency-005",
        "futures-stress-latency-002",
        "futures-stress-latency-005"
    ];

    public static DirectionalRuleV31SimulationProfile BuildProfile(DirectionalRuleDefinition rule)
        => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.BuildProfile(rule);

    public static IReadOnlyList<RegimeConditionalFilter> BuildFilters(
        IReadOnlyList<RegimeDriftDiagnosticTrade> diagnosticTrades)
    {
        var btc30 = diagnosticTrades.Select(t => t.BtcReturn30mPercent).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        var atr = diagnosticTrades.Select(t => t.AtrPercent).ToArray();
        var slope = diagnosticTrades.Select(t => t.TrendSlopePercent).ToArray();

        var btc30Q3Lower = Percentile(btc30, 2m / 3m);
        var atrMedian = Percentile(atr, 0.5m);
        var atrQ2Upper = Percentile(atr, 2m / 3m);
        var atrP90 = Percentile(atr, 0.9m);
        var slopeMedian = Percentile(slope, 0.5m);
        var slopeQ1Upper = Percentile(slope, 1m / 3m);

        bool Btc30(decimal threshold, RegimeDriftDiagnosticTrade t) => t.BtcReturn30mPercent.HasValue && t.BtcReturn30mPercent.Value > threshold;
        bool Btc60(decimal threshold, RegimeDriftDiagnosticTrade t) => t.BtcReturn60mPercent.HasValue && t.BtcReturn60mPercent.Value > threshold;
        bool Btc30Q3(RegimeDriftDiagnosticTrade t) => t.BtcReturn30mPercent.HasValue && t.BtcReturn30mPercent.Value >= btc30Q3Lower;

        var filters = new List<RegimeConditionalFilter>
        {
            new("Baseline", "Baseline", "All Rule01 short trades (no activation filter)", _ => true),

            // BTC momentum only
            new("Btc30Pos", "BtcMomentum", "BtcReturn30mPercent > 0", t => Btc30(0m, t)),
            new("Btc30Gt010", "BtcMomentum", "BtcReturn30mPercent > 0.10", t => Btc30(0.10m, t)),
            new("Btc30Gt020", "BtcMomentum", "BtcReturn30mPercent > 0.20", t => Btc30(0.20m, t)),
            new("Btc30Q3", "BtcMomentum", $"BtcReturn30mPercent in Q3 bucket (>= {btc30Q3Lower:F4})", Btc30Q3),
            new("Btc60Pos", "BtcMomentum", "BtcReturn60mPercent > 0", t => Btc60(0m, t)),
            new("Btc60Gt030", "BtcMomentum", "BtcReturn60mPercent > 0.30", t => Btc60(0.30m, t)),
            new("Btc60Gt050", "BtcMomentum", "BtcReturn60mPercent > 0.50", t => Btc60(0.50m, t)),
            new("Btc30Q3_Btc60Pos", "BtcMomentum", "BtcReturn30m Q3 AND BtcReturn60mPercent > 0", t => Btc30Q3(t) && Btc60(0m, t)),

            // Volatility only
            new("VolNormal", "Volatility", "VolatilityRegime = Normal", t => string.Equals(t.VolatilityRegime, "Normal", StringComparison.OrdinalIgnoreCase)),
            new("VolElevated", "Volatility", "VolatilityRegime = Elevated", t => string.Equals(t.VolatilityRegime, "Elevated", StringComparison.OrdinalIgnoreCase)),
            new("AtrBelowMedian", "Volatility", $"AtrPercent below recent median ({atrMedian:F4})", t => t.AtrPercent < atrMedian),
            new("AtrQ1Q2", "Volatility", $"AtrPercent in Q1/Q2 only (<= {atrQ2Upper:F4})", t => t.AtrPercent <= atrQ2Upper),
            new("AtrExcludeExtreme", "Volatility", $"Exclude extreme ATR (AtrPercent <= P90 {atrP90:F4})", t => t.AtrPercent <= atrP90),

            // Trend / context
            new("BtcUpOnly", "TrendContext", "BtcTrendRegime = BtcUp", t => string.Equals(t.BtcTrendRegime, "BtcUp", StringComparison.OrdinalIgnoreCase)),
            new("BnbSlopeQ1", "TrendContext", $"BNB TrendSlopePercent in Q1 (<= {slopeQ1Upper:F4})", t => t.TrendSlopePercent <= slopeQ1Upper),
            new("ExcludeAdverseTrend", "TrendContext", $"Exclude strong adverse (up) trend (TrendSlopePercent <= median {slopeMedian:F4})", t => t.TrendSlopePercent <= slopeMedian),
            new("UsSession", "TrendContext", "SessionBucket = US", t => string.Equals(t.SessionBucket, "US", StringComparison.OrdinalIgnoreCase)),

            // Controlled combinations
            new("Btc30Pos_VolNormal", "Combined", "BtcReturn30m > 0 AND VolatilityRegime = Normal", t => Btc30(0m, t) && string.Equals(t.VolatilityRegime, "Normal", StringComparison.OrdinalIgnoreCase)),
            new("Btc30Q3_VolNormal", "Combined", "BtcReturn30m Q3 AND VolatilityRegime = Normal (drift-study best)", t => Btc30Q3(t) && string.Equals(t.VolatilityRegime, "Normal", StringComparison.OrdinalIgnoreCase)),
            new("Btc30Pos_AtrBelowMedian", "Combined", "BtcReturn30m > 0 AND AtrPercent below median", t => Btc30(0m, t) && t.AtrPercent < atrMedian),
            new("Btc30Pos_BnbSlopeBelowMedian", "Combined", "BtcReturn30m > 0 AND BNB TrendSlopePercent <= median", t => Btc30(0m, t) && t.TrendSlopePercent <= slopeMedian),
            new("Btc30Pos_UsSession", "Combined", "BtcReturn30m > 0 AND SessionBucket = US", t => Btc30(0m, t) && string.Equals(t.SessionBucket, "US", StringComparison.OrdinalIgnoreCase))
        };

        return filters;
    }

    public static decimal Percentile(IReadOnlyList<decimal> values, decimal fraction)
    {
        if (values.Count == 0)
            return 0m;
        var sorted = values.OrderBy(v => v).ToArray();
        var idx = (int)Math.Floor(fraction * sorted.Length);
        idx = Math.Clamp(idx, 0, sorted.Length - 1);
        return sorted[idx];
    }
}
