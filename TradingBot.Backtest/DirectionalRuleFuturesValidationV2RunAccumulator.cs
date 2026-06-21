using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesValidationV2RunAccumulator
{
    private const int MedianReservoirCap = 512;
    private readonly Dictionary<string, ScanMetaBucket> _scanMeta = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TradeBucket> _tradeBuckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DirectionalRuleV2OverlapAnalysisRow> _overlapRows = [];

    public long ExecutedTradeCount { get; private set; }
    public long SkippedSignalCount { get; private set; }

    public void IngestScanResult(
        DirectionalRuleV2SimulationProfile profile,
        string windowLabel,
        DirectionalRuleV2ScanResult scan)
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

    public void IngestExpandedBatch(IReadOnlyList<DirectionalRuleV2TradeRecord> trades)
    {
        ExecutedTradeCount += trades.Count;
        foreach (var trade in trades)
            IngestExpandedTrade(trade);
    }

    public void AddOverlapRows(IReadOnlyList<DirectionalRuleV2OverlapAnalysisRow> rows)
        => _overlapRows.AddRange(rows);

    private void IngestExpandedTrade(DirectionalRuleV2TradeRecord trade)
    {
        if (string.Equals(trade.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            return;

        var key = DirectionalRuleFuturesValidationV2Aggregator.SummaryKey(trade);
        if (!_tradeBuckets.TryGetValue(key, out var bucket))
        {
            bucket = new TradeBucket(trade);
            _tradeBuckets[key] = bucket;
        }

        bucket.Add(trade);
    }

    public IReadOnlyList<DirectionalRuleV2SummaryRow> BuildSummaries()
    {
        var rows = new List<DirectionalRuleV2SummaryRow>();
        foreach (var tradeBucket in _tradeBuckets.Values)
        {
            var metaKey = ScanMetaKey(tradeBucket.ProfileKey, tradeBucket.WindowLabel);
            _scanMeta.TryGetValue(metaKey, out var meta);
            rows.Add(tradeBucket.ToSummaryRow(meta));
        }

        return rows
            .OrderBy(r => r.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.WindowLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<DirectionalRuleV2DrawdownRow> BuildDrawdownRows()
        => _tradeBuckets.Values
            .Select(b => b.ToDrawdownRow())
            .OrderBy(r => r.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.WindowLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<DirectionalRuleV2OverlapAnalysisRow> OverlapRows => _overlapRows;

    internal static string ScanMetaKey(string profileKey, string windowLabel)
        => $"{profileKey}|{windowLabel}";

    internal sealed class ScanMetaBucket
    {
        public ScanMetaBucket(DirectionalRuleV2SimulationProfile profile, string windowLabel)
        {
            ProfileKey = profile.ProfileKey;
            RuleName = profile.Rule.RuleName;
            Symbol = profile.Symbol;
            Interval = profile.Interval;
            WindowLabel = windowLabel;
            EntryMode = profile.EntryMode.ToString();
            OverlapPolicy = profile.OverlapPolicy.ToString();
            CooldownCandlesAfterExit = profile.CooldownCandlesAfterExit;
            MaxHoldMinutes = profile.MaxHoldMinutes;
        }

        public string ProfileKey { get; }
        public string RuleName { get; }
        public TradingSymbol Symbol { get; }
        public string Interval { get; }
        public string WindowLabel { get; }
        public string EntryMode { get; }
        public string OverlapPolicy { get; }
        public int CooldownCandlesAfterExit { get; }
        public int MaxHoldMinutes { get; }
        public int SignalCount { get; private set; }
        public int ExecutedTrades { get; private set; }
        public int SkippedOverlapSignals { get; private set; }
        public int SkippedCooldownSignals { get; private set; }
        public int SkippedPrioritySignals { get; private set; }

        public void Add(DirectionalRuleV2ScanResult scan)
        {
            SignalCount += scan.SignalCount;
            ExecutedTrades += scan.Trades.Count;
            foreach (var skip in scan.Skipped)
            {
                if (string.Equals(skip.SkipReason, nameof(DirectionalRuleV2SkipReason.SkippedCooldown), StringComparison.OrdinalIgnoreCase))
                    SkippedCooldownSignals++;
                else if (string.Equals(skip.SkipReason, nameof(DirectionalRuleV2SkipReason.SkippedPriorityOtherRule), StringComparison.OrdinalIgnoreCase))
                    SkippedPrioritySignals++;
                else
                    SkippedOverlapSignals++;
            }
        }
    }

    internal sealed class TradeBucket
    {
        private readonly List<decimal> _netReservoir = [];
        private decimal _equity;
        private decimal _peakEquity;
        private decimal _maxDrawdownQuote;
        private int _maxConsecutiveLosses;
        private int _currentConsecutiveLosses;
        private decimal _worstTradeNet = decimal.MaxValue;
        private DateTime _lastTradeTimeUtc = DateTime.MinValue;

        public TradeBucket(DirectionalRuleV2TradeRecord first)
        {
            ProfileKey = first.ProfileKey;
            RuleName = first.RuleName;
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
        public string RuleName { get; }
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

        public void Add(DirectionalRuleV2TradeRecord trade)
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

            if (trade.TimeUtc >= _lastTradeTimeUtc)
            {
                _lastTradeTimeUtc = trade.TimeUtc;
                if (trade.NetPnlQuote < 0m)
                {
                    _currentConsecutiveLosses++;
                    _maxConsecutiveLosses = Math.Max(_maxConsecutiveLosses, _currentConsecutiveLosses);
                }
                else
                {
                    _currentConsecutiveLosses = 0;
                }

                _equity += trade.NetPnlQuote;
                if (_equity > _peakEquity)
                    _peakEquity = _equity;
                _maxDrawdownQuote = Math.Max(_maxDrawdownQuote, _peakEquity - _equity);
                _worstTradeNet = Math.Min(_worstTradeNet, trade.NetPnlQuote);
            }
        }

        public DirectionalRuleV2SummaryRow ToSummaryRow(ScanMetaBucket? meta)
        {
            var avg = TradeCount == 0 ? (decimal?)null : Math.Round(NetPnlQuote / TradeCount, 8);
            var avgWin = WinCount == 0 ? (decimal?)null : Math.Round(WinSum / WinCount, 8);
            var lossCount = TradeCount - WinCount;
            var avgLoss = lossCount == 0 || LossSum == 0m
                ? (decimal?)null
                : Math.Round(LossSum / lossCount, 8);
            var profitFactor = GrossLosses == 0m
                ? GrossWins > 0m ? (decimal?)999m : null
                : Math.Round(GrossWins / GrossLosses, 6);
            var winRate = TradeCount == 0 ? 0m : Math.Round((decimal)WinCount / TradeCount, 6);

            return new DirectionalRuleV2SummaryRow
            {
                ProfileKey = ProfileKey,
                RuleName = RuleName,
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
                ExecutedTrades = meta?.ExecutedTrades ?? TradeCount,
                SkippedOverlapSignals = meta?.SkippedOverlapSignals ?? 0,
                SkippedCooldownSignals = meta?.SkippedCooldownSignals ?? 0,
                SkippedPrioritySignals = meta?.SkippedPrioritySignals ?? 0,
                GrossPnlQuote = GrossPnlQuote,
                NetPnlQuote = NetPnlQuote,
                AvgNetPnlPerTrade = avg,
                MedianNetPerTrade = Median(_netReservoir),
                WinRate = winRate,
                AverageWin = avgWin,
                AverageLoss = avgLoss,
                ProfitFactor = profitFactor,
                AggregateNetPositive = NetPnlQuote >= 0m,
                Verdict = DirectionalRuleFuturesValidationV2Aggregator.ClassifySummaryVerdict(TradeCount, NetPnlQuote, avg ?? 0m)
            };
        }

        public DirectionalRuleV2DrawdownRow ToDrawdownRow()
        {
            var avg = TradeCount == 0 ? (decimal?)null : Math.Round(NetPnlQuote / TradeCount, 8);
            var avgWin = WinCount == 0 ? (decimal?)null : Math.Round(WinSum / WinCount, 8);
            var lossCount = TradeCount - WinCount;
            var avgLoss = lossCount == 0 || LossSum == 0m
                ? (decimal?)null
                : Math.Round(LossSum / lossCount, 8);
            var profitFactor = GrossLosses == 0m
                ? GrossWins > 0m ? (decimal?)999m : null
                : Math.Round(GrossWins / GrossLosses, 6);
            var winRate = TradeCount == 0 ? 0m : Math.Round((decimal)WinCount / TradeCount, 6);

            return new DirectionalRuleV2DrawdownRow
            {
                ProfileKey = ProfileKey,
                RuleName = RuleName,
                Symbol = Symbol,
                Interval = Interval,
                WindowLabel = WindowLabel,
                EntryMode = EntryMode,
                OverlapPolicy = OverlapPolicy,
                CooldownCandlesAfterExit = CooldownCandlesAfterExit,
                MaxHoldMinutes = MaxHoldMinutes,
                CostScenarioLabel = CostScenarioLabel,
                TradeCount = TradeCount,
                MaxConsecutiveLosses = _maxConsecutiveLosses,
                MaxDrawdownQuote = Math.Round(_maxDrawdownQuote, 8),
                WorstTradeNet = _worstTradeNet == decimal.MaxValue ? 0m : _worstTradeNet,
                ProfitFactor = profitFactor,
                WinRate = winRate,
                AverageWin = avgWin,
                AverageLoss = avgLoss,
                MedianNetPerTrade = Median(_netReservoir)
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

    }
}
