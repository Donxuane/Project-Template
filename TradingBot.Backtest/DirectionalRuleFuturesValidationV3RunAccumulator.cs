using System.Globalization;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesValidationV3RunAccumulator
{
    private const int MedianReservoirCap = 512;
    private readonly Dictionary<string, ScanMetaBucket> _scanMeta = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TradeBucket> _tradeBuckets = new(StringComparer.OrdinalIgnoreCase);

    public long ExecutedTradeCount { get; private set; }
    public long SkippedSignalCount { get; private set; }

    public void IngestScanResult(
        DirectionalRuleV3SimulationProfile profile,
        string windowLabel,
        DirectionalRuleV3ScanResult scan)
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

    public void IngestExpandedBatch(IReadOnlyList<DirectionalRuleV3TradeRecord> trades)
    {
        ExecutedTradeCount += trades.Count;
        foreach (var trade in trades)
            IngestExpandedTrade(trade);
    }

    private void IngestExpandedTrade(DirectionalRuleV3TradeRecord trade)
    {
        if (string.Equals(trade.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            return;

        var key = DirectionalRuleFuturesValidationV3Aggregator.TradeBucketKey(trade);
        if (!_tradeBuckets.TryGetValue(key, out var bucket))
        {
            bucket = new TradeBucket(trade);
            _tradeBuckets[key] = bucket;
        }

        bucket.Add(trade);
    }

    public IReadOnlyList<DirectionalRuleV3FocusedSummaryRow> BuildSummaries()
        => _tradeBuckets.Values
            .Select(b =>
            {
                _scanMeta.TryGetValue(ScanMetaKey(b.ProfileKey, b.WindowLabel), out var meta);
                return b.ToSummaryRow(meta);
            })
            .OrderBy(r => r.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.WindowLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<DirectionalRuleV3DrawdownRow> BuildDrawdownRows()
        => _tradeBuckets.Values
            .Select(b => b.ToDrawdownRow())
            .OrderBy(r => r.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.WindowLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static string ScanMetaKey(string profileKey, string windowLabel)
        => $"{profileKey}|{windowLabel}";

    internal sealed class ScanMetaBucket
    {
        public ScanMetaBucket(DirectionalRuleV3SimulationProfile profile, string windowLabel)
        {
            ProfileKey = profile.ProfileKey;
            VariantLabel = profile.VariantLabel;
            IsPrimaryCandidate = profile.IsPrimaryCandidate;
            IsSmokeBestCandidate = profile.IsSmokeBestCandidate;
            WindowLabel = windowLabel;
        }

        public string ProfileKey { get; }
        public string VariantLabel { get; }
        public bool IsPrimaryCandidate { get; }
        public bool IsSmokeBestCandidate { get; }
        public string WindowLabel { get; }
        public int SignalCount { get; private set; }
        public int ExecutedTrades { get; private set; }
        public int SkippedOverlapSignals { get; private set; }
        public int SkippedCooldownSignals { get; private set; }

        public void Add(DirectionalRuleV3ScanResult scan)
        {
            SignalCount += scan.SignalCount;
            ExecutedTrades += scan.Trades.Count;
            foreach (var skip in scan.Skipped)
            {
                if (string.Equals(skip.SkipReason, nameof(DirectionalRuleV2SkipReason.SkippedCooldown), StringComparison.OrdinalIgnoreCase))
                    SkippedCooldownSignals++;
                else
                    SkippedOverlapSignals++;
            }
        }
    }

    internal sealed class TradeBucket
    {
        private readonly List<decimal> _netReservoir = [];
        private readonly List<(DateTime TimeUtc, decimal Net)> _orderedNets = [];

        public TradeBucket(DirectionalRuleV3TradeRecord first)
        {
            ProfileKey = first.ProfileKey;
            VariantLabel = first.VariantLabel;
            IsPrimaryCandidate = first.IsPrimaryCandidate;
            IsSmokeBestCandidate = first.IsSmokeBestCandidate;
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
        public bool IsPrimaryCandidate { get; }
        public bool IsSmokeBestCandidate { get; }
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

        public void Add(DirectionalRuleV3TradeRecord trade)
        {
            TradeCount++;
            GrossPnlQuote += trade.GrossPnlQuote;
            NetPnlQuote += trade.NetPnlQuote;
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
            _orderedNets.Add((trade.EntryTimeUtc, trade.NetPnlQuote));
        }

        public DirectionalRuleV3FocusedSummaryRow ToSummaryRow(ScanMetaBucket? meta)
        {
            var avg = TradeCount == 0 ? (decimal?)null : Math.Round(NetPnlQuote / TradeCount, 8);
            var lossCount = TradeCount - WinCount;
            return new DirectionalRuleV3FocusedSummaryRow
            {
                ProfileKey = ProfileKey,
                VariantLabel = VariantLabel,
                IsPrimaryCandidate = IsPrimaryCandidate,
                IsSmokeBestCandidate = IsSmokeBestCandidate,
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
                SkippedOverlapSignals = meta?.SkippedOverlapSignals ?? 0,
                SkippedCooldownSignals = meta?.SkippedCooldownSignals ?? 0,
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
                AggregatePositive = NetPnlQuote >= 0m,
                Verdict = DirectionalRuleFuturesValidationV3Aggregator.ClassifySummaryVerdict(TradeCount, NetPnlQuote, avg ?? 0m)
            };
        }

        public DirectionalRuleV3DrawdownRow ToDrawdownRow()
        {
            var ordered = _orderedNets.OrderBy(t => t.TimeUtc).ToArray();
            var risk = ComputeRiskMetrics(ordered);
            return new DirectionalRuleV3DrawdownRow
            {
                ProfileKey = ProfileKey,
                VariantLabel = VariantLabel,
                IsPrimaryCandidate = IsPrimaryCandidate,
                IsSmokeBestCandidate = IsSmokeBestCandidate,
                WindowLabel = WindowLabel,
                EntryMode = EntryMode,
                OverlapPolicy = OverlapPolicy,
                CooldownCandlesAfterExit = CooldownCandlesAfterExit,
                MaxHoldMinutes = MaxHoldMinutes,
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
                LongestFlatPeriodDays = risk.LongestFlatPeriodDays,
                LargestGivebackFromPeak = risk.LargestGivebackFromPeak,
                NetPnlByWeek = risk.NetPnlByWeek,
                NetPnlByMonth = risk.NetPnlByMonth
            };
        }

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

            var byWeek = new Dictionary<string, (int Count, decimal Net)>(StringComparer.OrdinalIgnoreCase);
            var byMonth = new Dictionary<string, (int Count, decimal Net)>(StringComparer.OrdinalIgnoreCase);

            foreach (var (timeUtc, net) in ordered)
            {
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
                Accumulate(byWeek, weekKey, net);
                Accumulate(byMonth, monthKey, net);
            }

            return new RiskMetrics
            {
                MaxConsecutiveLosses = maxLossStreak,
                MaxDrawdownQuote = Math.Round(maxDd, 8),
                WorstTradeNet = worstTrade,
                LongestFlatPeriodDays = longestFlatDays,
                LargestGivebackFromPeak = Math.Round(largestGiveback, 8),
                NetPnlByWeek = ToBuckets(byWeek),
                NetPnlByMonth = ToBuckets(byMonth)
            };
        }

        private static void Accumulate(Dictionary<string, (int Count, decimal Net)> dict, string key, decimal net)
        {
            if (!dict.TryGetValue(key, out var bucket))
                bucket = (0, 0m);
            dict[key] = (bucket.Count + 1, bucket.Net + net);
        }

        private static IReadOnlyList<DirectionalRuleV3PeriodBucketRow> ToBuckets(
            Dictionary<string, (int Count, decimal Net)> dict)
            => dict.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new DirectionalRuleV3PeriodBucketRow(kv.Key, kv.Value.Count, Math.Round(kv.Value.Net, 8)))
                .ToArray();

        private sealed class RiskMetrics
        {
            public int MaxConsecutiveLosses { get; init; }
            public decimal MaxDrawdownQuote { get; init; }
            public decimal WorstTradeNet { get; init; }
            public int LongestFlatPeriodDays { get; init; }
            public decimal LargestGivebackFromPeak { get; init; }
            public IReadOnlyList<DirectionalRuleV3PeriodBucketRow> NetPnlByWeek { get; init; } = [];
            public IReadOnlyList<DirectionalRuleV3PeriodBucketRow> NetPnlByMonth { get; init; } = [];
        }
    }
}
