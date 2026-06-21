using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesSimulationV1RunAccumulator
{
    private const int MedianReservoirCap = 256;
    private readonly Dictionary<string, SummaryBucket> _summaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CostBucket> _costBuckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (decimal Net, int Trades)> _entryModeModerate = new(StringComparer.OrdinalIgnoreCase);

    public long BaseTradeCount { get; private set; }
    public long ExpandedTradeCount { get; private set; }
    public decimal ModerateNetPnlQuote { get; private set; }
    public int ModerateTradeCount { get; private set; }

    public void AddBaseTrades(int count) => BaseTradeCount += count;

    public void IngestExpandedBatch(IReadOnlyList<DirectionalRuleFuturesTradeRecord> trades)
    {
        ExpandedTradeCount += trades.Count;
        foreach (var trade in trades)
            IngestExpandedTrade(trade);
    }

    private void IngestExpandedTrade(DirectionalRuleFuturesTradeRecord trade)
    {
        if (string.Equals(trade.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_summaries.TryGetValue(DirectionalRuleFuturesSimulationV1Aggregator.SummaryKey(trade), out var summary))
        {
            summary = new SummaryBucket(trade);
            _summaries[DirectionalRuleFuturesSimulationV1Aggregator.SummaryKey(trade)] = summary;
        }

        summary.Add(trade);

        var costKey = $"{trade.RuleName}|{trade.Direction}|{trade.CostScenarioLabel}";
        if (!_costBuckets.TryGetValue(costKey, out var cost))
        {
            cost = new CostBucket(trade.RuleName, trade.Direction, trade.CostScenarioLabel);
            _costBuckets[costKey] = cost;
        }

        cost.Add(trade.NetPnlQuote);

        if (string.Equals(trade.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
        {
            ModerateNetPnlQuote += trade.NetPnlQuote;
            ModerateTradeCount++;
            if (!_entryModeModerate.TryGetValue(trade.EntryMode, out var entryStats))
                entryStats = (0m, 0);
            _entryModeModerate[trade.EntryMode] = (entryStats.Net + trade.NetPnlQuote, entryStats.Trades + 1);
        }
    }

    public IReadOnlyList<DirectionalRuleFuturesSummaryRow> BuildSummaries()
        => _summaries.Values
            .Select(b => b.ToRow())
            .OrderBy(r => r.RuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Symbol)
            .ThenBy(r => r.Interval, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<DirectionalRuleFuturesRulePerformanceRow> BuildRulePerformance()
        => _summaries.Values
            .GroupBy(b => b.PerformanceKey, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var tradeCount = g.Sum(x => x.TradeCount);
                var net = g.Sum(x => x.NetPnlQuote);
                var gross = g.Sum(x => x.GrossPnlQuote);
                var profitTarget = g.Sum(x => x.ProfitTargetCount);
                var stopLoss = g.Sum(x => x.StopLossCount);
                var timeStop = g.Sum(x => x.TimeStopCount);
                return new DirectionalRuleFuturesRulePerformanceRow
                {
                    RuleName = first.RuleName,
                    Direction = first.Direction,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    EntryMode = first.EntryMode,
                    TargetPercent = first.TargetPercent,
                    StopPercent = first.StopPercent,
                    MaxHoldMinutes = first.MaxHoldMinutes,
                    CostScenarioLabel = first.CostScenarioLabel,
                    TradeCount = tradeCount,
                    NetPnlQuote = net,
                    GrossPnlQuote = gross,
                    AvgNetPnlPerTrade = tradeCount == 0 ? null : Math.Round(net / tradeCount, 8),
                    ProfitTargetRate = tradeCount == 0 ? 0m : Math.Round((decimal)profitTarget / tradeCount, 6),
                    StopLossRate = tradeCount == 0 ? 0m : Math.Round((decimal)stopLoss / tradeCount, 6),
                    TimeStopRate = tradeCount == 0 ? 0m : Math.Round((decimal)timeStop / tradeCount, 6),
                    Verdict = DirectionalRuleFuturesSimulationV1Aggregator.ClassifyRuleVerdictPublic(tradeCount, net)
                };
            })
            .OrderByDescending(r => r.NetPnlQuote)
            .ToArray();

    public IReadOnlyList<DirectionalRuleFuturesCostSensitivityRow> BuildCostSensitivity()
    {
        var scenarios = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios()
            .Where(s => DirectionalRuleFuturesSimulationV1Simulator.SimulationCostScenarioLabels
                .Contains(s.Label, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(s => s.Label, StringComparer.OrdinalIgnoreCase);

        return _costBuckets.Values
            .Select(b =>
            {
                scenarios.TryGetValue(b.CostScenarioLabel, out var scenario);
                scenario ??= LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios().First();
                var avg = b.TradeCount == 0 ? (decimal?)null : Math.Round(b.NetPnlQuote / b.TradeCount, 8);
                return new DirectionalRuleFuturesCostSensitivityRow
                {
                    RuleName = b.RuleName,
                    Direction = b.Direction,
                    CostScenarioLabel = b.CostScenarioLabel,
                    RoundTripCostPercent = RangeExpansionV2FeasibilityCostModel.EstimateRoundTripCostPercent(scenario),
                    FundingRatePercentPerHour = scenario.FundingRatePercentPerHour,
                    TradeCount = b.TradeCount,
                    NetPnlQuote = b.NetPnlQuote,
                    AvgNetPnlPerTrade = avg,
                    MedianNetPnlPerTrade = b.MedianNet(),
                    Verdict = DirectionalRuleFuturesSimulationV1Aggregator.ClassifyRuleVerdictPublic(b.TradeCount, b.NetPnlQuote)
                };
            })
            .OrderBy(r => r.RuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyDictionary<string, (decimal Net, int Trades)> EntryModeModerateStats()
        => _entryModeModerate;

    private sealed class SummaryBucket
    {
        private readonly List<decimal> _netReservoir = [];

        public SummaryBucket(DirectionalRuleFuturesTradeRecord first)
        {
            RuleName = first.RuleName;
            Direction = first.Direction;
            Symbol = first.Symbol;
            Interval = first.Interval;
            WindowLabel = first.WindowLabel;
            EntryMode = first.EntryMode;
            TargetPercent = first.TargetPercent;
            StopPercent = first.StopPercent;
            MaxHoldMinutes = first.MaxHoldMinutes;
            CostScenarioLabel = first.CostScenarioLabel;
        }

        public string RuleName { get; }
        public LongShortDirection Direction { get; }
        public TradingSymbol Symbol { get; }
        public string Interval { get; }
        public string WindowLabel { get; }
        public string EntryMode { get; }
        public decimal TargetPercent { get; }
        public decimal StopPercent { get; }
        public int MaxHoldMinutes { get; }
        public string CostScenarioLabel { get; }
        public int TradeCount { get; private set; }
        public int NetWinnerCount { get; private set; }
        public decimal GrossPnlQuote { get; private set; }
        public decimal NetPnlQuote { get; private set; }
        public int ProfitTargetCount { get; private set; }
        public int StopLossCount { get; private set; }
        public int TimeStopCount { get; private set; }

        public string PerformanceKey
            => $"{RuleName}|{Direction}|{Symbol}|{Interval}|{EntryMode}|{TargetPercent}|{StopPercent}|{MaxHoldMinutes}|{CostScenarioLabel}";

        public void Add(DirectionalRuleFuturesTradeRecord trade)
        {
            TradeCount++;
            if (trade.NetPnlQuote > 0m)
                NetWinnerCount++;
            GrossPnlQuote += trade.GrossPnlQuote;
            NetPnlQuote += trade.NetPnlQuote;
            if (string.Equals(trade.ExitReason, "ProfitTarget", StringComparison.OrdinalIgnoreCase))
                ProfitTargetCount++;
            if (string.Equals(trade.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase))
                StopLossCount++;
            if (string.Equals(trade.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase))
                TimeStopCount++;
            if (_netReservoir.Count < MedianReservoirCap)
                _netReservoir.Add(trade.NetPnlQuote);
        }

        public DirectionalRuleFuturesSummaryRow ToRow()
        {
            var avg = TradeCount == 0 ? (decimal?)null : Math.Round(NetPnlQuote / TradeCount, 8);
            decimal? median = null;
            if (_netReservoir.Count > 0)
            {
                var sorted = _netReservoir.OrderBy(v => v).ToArray();
                var mid = sorted.Length / 2;
                median = sorted.Length % 2 == 0
                    ? Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 8)
                    : Math.Round(sorted[mid], 8);
            }

            return new DirectionalRuleFuturesSummaryRow
            {
                RuleName = RuleName,
                Direction = Direction,
                Symbol = Symbol,
                Interval = Interval,
                WindowLabel = WindowLabel,
                EntryMode = EntryMode,
                TargetPercent = TargetPercent,
                StopPercent = StopPercent,
                MaxHoldMinutes = MaxHoldMinutes,
                CostScenarioLabel = CostScenarioLabel,
                TradeCount = TradeCount,
                NetWinnerCount = NetWinnerCount,
                GrossPnlQuote = GrossPnlQuote,
                NetPnlQuote = NetPnlQuote,
                AvgNetPnlPerTrade = avg,
                MedianNetPnlPerTrade = median,
                ProfitTargetRate = TradeCount == 0 ? 0m : Math.Round((decimal)ProfitTargetCount / TradeCount, 6),
                StopLossRate = TradeCount == 0 ? 0m : Math.Round((decimal)StopLossCount / TradeCount, 6),
                TimeStopRate = TradeCount == 0 ? 0m : Math.Round((decimal)TimeStopCount / TradeCount, 6),
                Verdict = DirectionalRuleFuturesSimulationV1Aggregator.ClassifySummaryVerdictPublic(TradeCount, NetPnlQuote, avg ?? 0m)
            };
        }
    }

    private sealed class CostBucket(string ruleName, LongShortDirection direction, string costScenarioLabel)
    {
        private readonly List<decimal> _netReservoir = [];
        public string RuleName { get; } = ruleName;
        public LongShortDirection Direction { get; } = direction;
        public string CostScenarioLabel { get; } = costScenarioLabel;
        public int TradeCount { get; private set; }
        public decimal NetPnlQuote { get; private set; }

        public void Add(decimal netPnl)
        {
            TradeCount++;
            NetPnlQuote += netPnl;
            if (_netReservoir.Count < MedianReservoirCap)
                _netReservoir.Add(netPnl);
        }

        public decimal? MedianNet()
        {
            if (_netReservoir.Count == 0)
                return null;
            var sorted = _netReservoir.OrderBy(v => v).ToArray();
            var mid = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 8)
                : Math.Round(sorted[mid], 8);
        }
    }
}
