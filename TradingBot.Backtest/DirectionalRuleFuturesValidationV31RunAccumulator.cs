using System.Globalization;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesValidationV31RunAccumulator
{
    private const int MedianReservoirCap = 512;
    private readonly Dictionary<string, ScanMetaBucket> _scanMeta = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TradeBucket> _tradeBuckets = new(StringComparer.OrdinalIgnoreCase);

    public long ExecutedTradeCount { get; private set; }
    public long SkippedSignalCount { get; private set; }

    public void IngestScanResult(
        DirectionalRuleV31SimulationProfile profile,
        string windowLabel,
        DirectionalRuleV31ScanResult scan)
    {
        var key = ScanMetaKey(profile.ProfileKey, windowLabel);
        if (!_scanMeta.TryGetValue(key, out var bucket))
        {
            bucket = new ScanMetaBucket(profile, windowLabel);
            _scanMeta[key] = bucket;
        }

        bucket.Add(scan);
        SkippedSignalCount += scan.Skipped.Count;
    }

    public void IngestExpandedBatch(IReadOnlyList<DirectionalRuleV31TradeRecord> trades)
    {
        ExecutedTradeCount += trades.Count;
        foreach (var trade in trades)
            IngestExpandedTrade(trade);
    }

    private void IngestExpandedTrade(DirectionalRuleV31TradeRecord trade)
    {
        if (string.Equals(trade.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            return;

        var key = DirectionalRuleFuturesValidationV31Aggregator.TradeBucketKey(trade);
        if (!_tradeBuckets.TryGetValue(key, out var bucket))
        {
            bucket = new TradeBucket(trade);
            _tradeBuckets[key] = bucket;
        }

        bucket.Add(trade);
    }

    public IReadOnlyList<DirectionalRuleV31SummaryRow> BuildSummaries()
        => _tradeBuckets.Values
            .Select(b =>
            {
                _scanMeta.TryGetValue(ScanMetaKey(b.ProfileKey, b.WindowLabel), out var meta);
                return b.ToSummaryRow(meta);
            })
            .OrderBy(r => r.ValidationTrack)
            .ThenBy(r => r.Symbol)
            .ThenBy(r => r.Interval, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.WindowLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<DirectionalRuleV31DrawdownRow> BuildDrawdownRows()
        => _tradeBuckets.Values.Select(b => b.ToDrawdownRow()).ToArray();

    public IReadOnlyList<DirectionalRuleV31MonthlyWeeklyPnlRow> BuildMonthlyWeeklyRows()
        => _tradeBuckets.Values.SelectMany(b => b.ToMonthlyWeeklyRows()).ToArray();

    internal static string ScanMetaKey(string profileKey, string windowLabel)
        => $"{profileKey}|{windowLabel}";

    internal sealed class ScanMetaBucket
    {
        public ScanMetaBucket(DirectionalRuleV31SimulationProfile profile, string windowLabel)
        {
            ProfileKey = profile.ProfileKey;
            SignalCount = 0;
            WindowLabel = windowLabel;
        }

        public string ProfileKey { get; }
        public string WindowLabel { get; }
        public int SignalCount { get; private set; }

        public void Add(DirectionalRuleV31ScanResult scan) => SignalCount += scan.SignalCount;
    }

    internal sealed class TradeBucket
    {
        private readonly List<decimal> _netReservoir = [];
        // Retain only the fields the risk/drawdown metrics need, not the full trade record.
        // Holding full records here is the dominant memory cost on large matrices.
        private readonly List<(DateTime TimeUtc, decimal Net)> _trades = [];

        public TradeBucket(DirectionalRuleV31TradeRecord first)
        {
            ProfileKey = first.ProfileKey;
            VariantLabel = first.VariantLabel;
            ValidationTrack = first.ValidationTrack;
            IsBestBnbCandidate = first.IsBestBnbCandidate;
            Symbol = first.Symbol;
            Interval = first.Interval;
            WindowLabel = first.WindowLabel;
            EntryMode = first.EntryMode;
            OverlapPolicy = first.OverlapPolicy;
            CooldownCandlesAfterExit = first.CooldownCandlesAfterExit;
            MaxHoldMinutes = first.MaxHoldMinutes;
            TargetPercent = first.TargetPercent;
            StopPercent = first.StopPercent;
            CostScenarioLabel = first.CostScenarioLabel;
        }

        public string ProfileKey { get; }
        public string VariantLabel { get; }
        public DirectionalRuleV31ValidationTrack ValidationTrack { get; }
        public bool IsBestBnbCandidate { get; }
        public TradingSymbol Symbol { get; }
        public string Interval { get; }
        public string WindowLabel { get; }
        public string EntryMode { get; }
        public string OverlapPolicy { get; }
        public int CooldownCandlesAfterExit { get; }
        public int MaxHoldMinutes { get; }
        public decimal TargetPercent { get; }
        public decimal StopPercent { get; }
        public string CostScenarioLabel { get; }
        public int TradeCount { get; private set; }
        public int WinCount { get; private set; }
        public decimal GrossPnlQuote { get; private set; }
        public decimal NetPnlQuote { get; private set; }
        public decimal GrossWins { get; private set; }
        public decimal GrossLosses { get; private set; }
        public decimal WinSum { get; private set; }
        public decimal LossSum { get; private set; }
        public decimal HoldMinutesSum { get; private set; }
        public int ProfitTargetCount { get; private set; }
        public int StopLossCount { get; private set; }
        public int TimeStopCount { get; private set; }

        public void Add(DirectionalRuleV31TradeRecord trade)
        {
            TradeCount++;
            _trades.Add((trade.EntryTimeUtc, trade.NetPnlQuote));
            GrossPnlQuote += trade.GrossPnlQuote;
            NetPnlQuote += trade.NetPnlQuote;
            HoldMinutesSum += trade.DurationMinutes;
            ClassifyExitReason(trade.ExitReason);

            if (trade.NetPnlQuote > 0m)
            {
                WinCount++;
                WinSum += trade.NetPnlQuote;
                GrossWins += trade.NetPnlQuote;
            }
            else if (trade.NetPnlQuote < 0m)
            {
                LossSum += trade.NetPnlQuote;
                GrossLosses += Math.Abs(trade.NetPnlQuote);
            }

            if (_netReservoir.Count < MedianReservoirCap)
                _netReservoir.Add(trade.NetPnlQuote);
        }

        private void ClassifyExitReason(string exitReason)
        {
            if (exitReason.Contains("Target", StringComparison.OrdinalIgnoreCase)
                || exitReason.Contains("Profit", StringComparison.OrdinalIgnoreCase))
                ProfitTargetCount++;
            else if (exitReason.Contains("Stop", StringComparison.OrdinalIgnoreCase))
                StopLossCount++;
            else if (exitReason.Contains("Time", StringComparison.OrdinalIgnoreCase)
                     || exitReason.Contains("Hold", StringComparison.OrdinalIgnoreCase)
                     || exitReason.Contains("MaxHold", StringComparison.OrdinalIgnoreCase))
                TimeStopCount++;
        }

        public DirectionalRuleV31SummaryRow ToSummaryRow(ScanMetaBucket? meta)
        {
            var avg = TradeCount == 0 ? (decimal?)null : Math.Round(NetPnlQuote / TradeCount, 8);
            var lossCount = TradeCount - WinCount;
            return new DirectionalRuleV31SummaryRow
            {
                ProfileKey = ProfileKey,
                VariantLabel = VariantLabel,
                ValidationTrack = ValidationTrack,
                IsBestBnbCandidate = IsBestBnbCandidate,
                Symbol = Symbol,
                Interval = Interval,
                WindowLabel = WindowLabel,
                EntryMode = EntryMode,
                OverlapPolicy = OverlapPolicy,
                CooldownCandlesAfterExit = CooldownCandlesAfterExit,
                TargetPercent = TargetPercent,
                StopPercent = StopPercent,
                MaxHoldMinutes = MaxHoldMinutes,
                CostScenarioLabel = CostScenarioLabel,
                SignalCount = meta?.SignalCount ?? 0,
                ExecutedTrades = TradeCount,
                GrossPnlQuote = GrossPnlQuote,
                NetPnlQuote = NetPnlQuote,
                AvgNetPnlPerTrade = avg,
                MedianNetPerTrade = Median(_netReservoir),
                WinRate = TradeCount == 0 ? 0m : Math.Round((decimal)WinCount / TradeCount, 6),
                AverageWin = WinCount == 0 ? null : Math.Round(WinSum / WinCount, 8),
                AverageLoss = lossCount == 0 || LossSum == 0m ? null : Math.Round(LossSum / lossCount, 8),
                ProfitFactor = GrossLosses == 0m
                    ? GrossWins > 0m ? 999m : null
                    : Math.Round(GrossWins / GrossLosses, 6),
                AverageHoldMinutes = TradeCount == 0 ? 0m : Math.Round(HoldMinutesSum / TradeCount, 2),
                TimeStopRate = Rate(TimeStopCount),
                StopLossRate = Rate(StopLossCount),
                ProfitTargetRate = Rate(ProfitTargetCount),
                SymbolPositive = NetPnlQuote >= 0m,
                TradeCountSufficient = TradeCount >= DirectionalRuleFuturesValidationV31Catalog.MinimumMeaningfulTrades,
                Verdict = DirectionalRuleFuturesValidationV31Aggregator.ClassifySummaryVerdict(TradeCount, NetPnlQuote, avg ?? 0m)
            };
        }

        public DirectionalRuleV31DrawdownRow ToDrawdownRow()
        {
            var ordered = _trades.OrderBy(t => t.TimeUtc).ToArray();
            var risk = ComputeRiskMetrics(ordered);
            return new DirectionalRuleV31DrawdownRow
            {
                ProfileKey = ProfileKey,
                VariantLabel = VariantLabel,
                ValidationTrack = ValidationTrack,
                IsBestBnbCandidate = IsBestBnbCandidate,
                Symbol = Symbol,
                Interval = Interval,
                WindowLabel = WindowLabel,
                CostScenarioLabel = CostScenarioLabel,
                TradeCount = TradeCount,
                MaxConsecutiveLosses = risk.MaxConsecutiveLosses,
                MaxDrawdownQuote = risk.MaxDrawdownQuote,
                WorstTradeNet = risk.WorstTradeNet,
                ProfitFactor = GrossLosses == 0m
                    ? GrossWins > 0m ? 999m : null
                    : Math.Round(GrossWins / GrossLosses, 6),
                WinRate = TradeCount == 0 ? 0m : Math.Round((decimal)WinCount / TradeCount, 6),
                AverageWin = WinCount == 0 ? null : Math.Round(WinSum / WinCount, 8),
                AverageLoss = TradeCount - WinCount == 0 || LossSum == 0m
                    ? null
                    : Math.Round(LossSum / (TradeCount - WinCount), 8),
                MedianNetPerTrade = Median(_netReservoir),
                AverageHoldMinutes = TradeCount == 0 ? 0m : Math.Round(HoldMinutesSum / TradeCount, 2),
                TimeStopRate = Rate(TimeStopCount),
                StopLossRate = Rate(StopLossCount),
                ProfitTargetRate = Rate(ProfitTargetCount),
                LongestFlatPeriodDays = risk.LongestFlatPeriodDays,
                LargestGivebackFromPeak = risk.LargestGivebackFromPeak,
                NetPnlByWeek = risk.NetPnlByWeek,
                NetPnlByMonth = risk.NetPnlByMonth
            };
        }

        public IEnumerable<DirectionalRuleV31MonthlyWeeklyPnlRow> ToMonthlyWeeklyRows()
        {
            var drawdown = ToDrawdownRow();
            foreach (var bucket in drawdown.NetPnlByWeek)
            {
                yield return new DirectionalRuleV31MonthlyWeeklyPnlRow
                {
                    ProfileKey = ProfileKey,
                    VariantLabel = VariantLabel,
                    ValidationTrack = ValidationTrack,
                    IsBestBnbCandidate = IsBestBnbCandidate,
                    Symbol = Symbol,
                    Interval = Interval,
                    WindowLabel = WindowLabel,
                    CostScenarioLabel = CostScenarioLabel,
                    PeriodType = "Week",
                    PeriodKey = bucket.PeriodKey,
                    TradeCount = bucket.TradeCount,
                    NetPnlQuote = bucket.NetPnlQuote,
                    MaxDrawdownQuote = bucket.MaxDrawdownQuote,
                    MaxConsecutiveLosses = bucket.MaxConsecutiveLosses
                };
            }

            foreach (var bucket in drawdown.NetPnlByMonth)
            {
                yield return new DirectionalRuleV31MonthlyWeeklyPnlRow
                {
                    ProfileKey = ProfileKey,
                    VariantLabel = VariantLabel,
                    ValidationTrack = ValidationTrack,
                    IsBestBnbCandidate = IsBestBnbCandidate,
                    Symbol = Symbol,
                    Interval = Interval,
                    WindowLabel = WindowLabel,
                    CostScenarioLabel = CostScenarioLabel,
                    PeriodType = "Month",
                    PeriodKey = bucket.PeriodKey,
                    TradeCount = bucket.TradeCount,
                    NetPnlQuote = bucket.NetPnlQuote,
                    MaxDrawdownQuote = bucket.MaxDrawdownQuote,
                    MaxConsecutiveLosses = bucket.MaxConsecutiveLosses
                };
            }
        }

        private decimal Rate(int count)
            => TradeCount == 0 ? 0m : Math.Round((decimal)count / TradeCount, 6);

        private static decimal? Median(IReadOnlyList<decimal> values)
        {
            if (values.Count == 0)
                return null;
            var sorted = values.OrderBy(v => v).ToArray();
            var mid = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 8)
                : Math.Round(sorted[mid], 8);
        }

        private static RiskMetrics ComputeRiskMetrics((DateTime TimeUtc, decimal Net)[] ordered)
        {
            if (ordered.Length == 0)
                return new RiskMetrics();

            decimal equity = 0m;
            decimal peak = 0m;
            decimal maxDd = 0m;
            decimal largestGiveback = 0m;
            var maxLossStreak = 0;
            var lossStreak = 0;
            var worstTrade = ordered[0].Net;
            DateTime? lastTrade = null;
            var longestFlatDays = 0;

            var byWeek = new Dictionary<string, List<(DateTime TimeUtc, decimal Net)>>(StringComparer.OrdinalIgnoreCase);
            var byMonth = new Dictionary<string, List<(DateTime TimeUtc, decimal Net)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var trade in ordered)
            {
                var net = trade.Net;
                var timeUtc = trade.TimeUtc;
                if (net < worstTrade)
                    worstTrade = net;
                if (net < 0m)
                {
                    lossStreak++;
                    maxLossStreak = Math.Max(maxLossStreak, lossStreak);
                }
                else
                {
                    lossStreak = 0;
                }

                equity += net;
                if (equity > peak)
                    peak = equity;
                var dd = peak - equity;
                if (dd > maxDd)
                    maxDd = dd;
                if (peak > 0m)
                    largestGiveback = Math.Max(largestGiveback, dd);

                if (lastTrade.HasValue)
                {
                    var gapDays = (int)(timeUtc - lastTrade.Value).TotalDays;
                    if (gapDays > longestFlatDays)
                        longestFlatDays = gapDays;
                }

                lastTrade = timeUtc;
                var weekKey = $"{timeUtc.Year}-W{ISOWeek.GetWeekOfYear(timeUtc):D2}";
                var monthKey = $"{timeUtc.Year}-{timeUtc.Month:D2}";
                AddPeriod(byWeek, weekKey, timeUtc, net);
                AddPeriod(byMonth, monthKey, timeUtc, net);
            }

            return new RiskMetrics
            {
                MaxConsecutiveLosses = maxLossStreak,
                MaxDrawdownQuote = Math.Round(maxDd, 8),
                WorstTradeNet = worstTrade,
                LongestFlatPeriodDays = longestFlatDays,
                LargestGivebackFromPeak = Math.Round(largestGiveback, 8),
                NetPnlByWeek = ToPeriodBuckets(byWeek),
                NetPnlByMonth = ToPeriodBuckets(byMonth)
            };
        }

        private static void AddPeriod(
            Dictionary<string, List<(DateTime TimeUtc, decimal Net)>> dict,
            string key,
            DateTime timeUtc,
            decimal net)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = [];
                dict[key] = list;
            }

            list.Add((timeUtc, net));
        }

        private static IReadOnlyList<DirectionalRuleV31PeriodBucketRow> ToPeriodBuckets(
            Dictionary<string, List<(DateTime TimeUtc, decimal Net)>> dict)
            => dict.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv =>
                {
                    var metrics = ComputePeriodRisk(kv.Value);
                    return new DirectionalRuleV31PeriodBucketRow(
                        kv.Key,
                        kv.Value.Count,
                        Math.Round(kv.Value.Sum(x => x.Net), 8),
                        metrics.MaxDrawdownQuote,
                        metrics.MaxConsecutiveLosses);
                })
                .ToArray();

        private static (decimal MaxDrawdownQuote, int MaxConsecutiveLosses) ComputePeriodRisk(
            IReadOnlyList<(DateTime TimeUtc, decimal Net)> trades)
        {
            decimal equity = 0m;
            decimal peak = 0m;
            decimal maxDd = 0m;
            var maxLossStreak = 0;
            var lossStreak = 0;
            foreach (var (_, net) in trades.OrderBy(t => t.TimeUtc))
            {
                if (net < 0m)
                {
                    lossStreak++;
                    maxLossStreak = Math.Max(maxLossStreak, lossStreak);
                }
                else
                {
                    lossStreak = 0;
                }

                equity += net;
                if (equity > peak)
                    peak = equity;
                maxDd = Math.Max(maxDd, peak - equity);
            }

            return (Math.Round(maxDd, 8), maxLossStreak);
        }

        private sealed class RiskMetrics
        {
            public int MaxConsecutiveLosses { get; init; }
            public decimal MaxDrawdownQuote { get; init; }
            public decimal WorstTradeNet { get; init; }
            public int LongestFlatPeriodDays { get; init; }
            public decimal LargestGivebackFromPeak { get; init; }
            public IReadOnlyList<DirectionalRuleV31PeriodBucketRow> NetPnlByWeek { get; init; } = [];
            public IReadOnlyList<DirectionalRuleV31PeriodBucketRow> NetPnlByMonth { get; init; } = [];
        }
    }
}
