using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService.Services;

public sealed class FeeProfitGuardExpectedMoveBlockObservability(
    ILogger<FeeProfitGuardExpectedMoveBlockObservability> logger) : IFeeProfitGuardExpectedMoveBlockObservability
{
    private readonly object _sync = new();
    private readonly Dictionary<TradingSymbol, SymbolBucket> _buckets = new();

    public void RecordExpectedMoveBlock(FeeProfitGuardExpectedMoveBlockObservation observation)
    {
        lock (_sync)
        {
            if (!_buckets.TryGetValue(observation.Symbol, out var bucket))
            {
                bucket = new SymbolBucket();
                _buckets[observation.Symbol] = bucket;
            }

            bucket.Record(observation);
        }
    }

    public void FlushAndLog(
        decimal currentMinExpectedMovePercent,
        decimal currentMinNetProfitPercent,
        TimeSpan reportingWindow)
    {
        var aggregates = DrainAggregates();
        LogAggregates(aggregates, currentMinExpectedMovePercent, currentMinNetProfitPercent, reportingWindow);
    }

    private IReadOnlyList<FeeProfitGuardExpectedMoveBlockAggregate> DrainAggregates()
    {
        lock (_sync)
        {
            if (_buckets.Count == 0)
                return [];

            var aggregates = _buckets.Values
                .Select(bucket => bucket.ToAggregate())
                .OrderBy(x => x.Symbol.ToString(), StringComparer.Ordinal)
                .ToArray();

            _buckets.Clear();
            return aggregates;
        }
    }

    private void LogAggregates(
        IReadOnlyList<FeeProfitGuardExpectedMoveBlockAggregate> aggregates,
        decimal currentMinExpectedMovePercent,
        decimal currentMinNetProfitPercent,
        TimeSpan reportingWindow)
    {
        if (aggregates.Count == 0)
        {
            logger.LogInformation(
                "FeeProfitGuard Spot OpenLong expected-move block aggregate: ReportingWindowMinutes={ReportingWindowMinutes}, SymbolsReported=0, CurrentMinExpectedMovePercent={CurrentMinExpectedMovePercent}, CurrentMinNetProfitPercent={CurrentMinNetProfitPercent}, Summary=No Spot OpenLong candidates blocked by expected-move threshold in reporting window.",
                Math.Round(reportingWindow.TotalMinutes, 2),
                currentMinExpectedMovePercent,
                currentMinNetProfitPercent);
            return;
        }

        foreach (var aggregate in aggregates)
        {
            logger.LogInformation(
                "FeeProfitGuard Spot OpenLong expected-move block aggregate: Symbol={Symbol}, TotalBlockedCandidates={TotalBlockedCandidates}, AvgExpectedMovePercent={AvgExpectedMovePercent}, MinExpectedMovePercent={MinExpectedMovePercent}, MaxExpectedMovePercent={MaxExpectedMovePercent}, AvgExpectedNetProfitPercent={AvgExpectedNetProfitPercent}, ExpectedTargetSource={ExpectedTargetSource}, AvgConfidence={AvgConfidence}, RejectionReason={RejectionReason}, CurrentMinExpectedMovePercent={CurrentMinExpectedMovePercent}, CurrentMinNetProfitPercent={CurrentMinNetProfitPercent}, ReportingWindowMinutes={ReportingWindowMinutes}",
                aggregate.Symbol,
                aggregate.TotalBlockedCandidates,
                aggregate.AvgExpectedMovePercent,
                aggregate.MinExpectedMovePercent,
                aggregate.MaxExpectedMovePercent,
                aggregate.AvgExpectedNetProfitPercent,
                aggregate.ExpectedTargetSourceBreakdown,
                aggregate.AvgConfidence,
                aggregate.RejectionReasonBreakdown,
                currentMinExpectedMovePercent,
                currentMinNetProfitPercent,
                Math.Round(reportingWindow.TotalMinutes, 2));
        }
    }

    private sealed class SymbolBucket
    {
        private int _count;
        private decimal _expectedMoveSum;
        private int _expectedMoveSampleCount;
        private decimal? _minExpectedMove;
        private decimal? _maxExpectedMove;
        private decimal _expectedNetProfitSum;
        private int _confidenceSampleCount;
        private decimal _confidenceSum;
        private readonly Dictionary<string, int> _targetSourceCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _rejectionReasonCounts = new(StringComparer.OrdinalIgnoreCase);
        private TradingSymbol _symbol;

        public void Record(FeeProfitGuardExpectedMoveBlockObservation observation)
        {
            _symbol = observation.Symbol;
            _count++;

            if (observation.ExpectedMovePercent.HasValue)
            {
                var expectedMove = observation.ExpectedMovePercent.Value;
                _expectedMoveSum += expectedMove;
                _expectedMoveSampleCount++;
                _minExpectedMove = _minExpectedMove.HasValue
                    ? Math.Min(_minExpectedMove.Value, expectedMove)
                    : expectedMove;
                _maxExpectedMove = _maxExpectedMove.HasValue
                    ? Math.Max(_maxExpectedMove.Value, expectedMove)
                    : expectedMove;
            }

            _expectedNetProfitSum += observation.ExpectedNetProfitPercent;

            if (observation.Confidence.HasValue)
            {
                _confidenceSum += observation.Confidence.Value;
                _confidenceSampleCount++;
            }

            var targetSource = string.IsNullOrWhiteSpace(observation.ExpectedTargetSource)
                ? "Unknown"
                : observation.ExpectedTargetSource.Trim();
            _targetSourceCounts[targetSource] = _targetSourceCounts.GetValueOrDefault(targetSource) + 1;

            var rejectionReason = string.IsNullOrWhiteSpace(observation.RejectionReason)
                ? "Unknown"
                : observation.RejectionReason.Trim();
            _rejectionReasonCounts[rejectionReason] = _rejectionReasonCounts.GetValueOrDefault(rejectionReason) + 1;
        }

        public FeeProfitGuardExpectedMoveBlockAggregate ToAggregate()
        {
            return new FeeProfitGuardExpectedMoveBlockAggregate
            {
                Symbol = _symbol,
                TotalBlockedCandidates = _count,
                AvgExpectedMovePercent = _expectedMoveSampleCount > 0
                    ? Math.Round(_expectedMoveSum / _expectedMoveSampleCount, 4)
                    : null,
                MinExpectedMovePercent = _minExpectedMove,
                MaxExpectedMovePercent = _maxExpectedMove,
                AvgExpectedNetProfitPercent = _count > 0
                    ? Math.Round(_expectedNetProfitSum / _count, 4)
                    : 0m,
                ExpectedTargetSourceBreakdown = FormatBreakdown(_targetSourceCounts),
                AvgConfidence = _confidenceSampleCount > 0
                    ? Math.Round(_confidenceSum / _confidenceSampleCount, 4)
                    : null,
                RejectionReasonBreakdown = FormatBreakdown(_rejectionReasonCounts)
            };
        }

        private static string FormatBreakdown(IReadOnlyDictionary<string, int> counts)
        {
            if (counts.Count == 0)
                return "Unknown";

            var builder = new StringBuilder();
            foreach (var pair in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (builder.Length > 0)
                    builder.Append(';');

                builder.Append(pair.Key);
                builder.Append(':');
                builder.Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
