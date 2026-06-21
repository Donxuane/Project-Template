namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic-only activation simulator for BNB15 lookback starvation variants.
/// Does not modify frozen profile logic.
/// </summary>
public static class Bnb15LookbackStarvationStudySimulator
{
    public sealed record DiagnosticSimOutcome(
        IReadOnlyList<RegimeDriftDiagnosticTrade> TakenTrades,
        int ActivatedCheckpointCount,
        int TotalCheckpoints);

    public static DiagnosticSimOutcome Simulate(
        CrossSymbolComboKey key,
        int checkpointFrequencyHours,
        int activationPeriodHours,
        int lookbackDays,
        int minLookbackTrades,
        bool requireNetPositive,
        bool requireStressPlusPositive,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> stressTrades,
        DateTime studyStartUtc,
        DateTime studyEndUtc)
    {
        var moderateOrdered = moderateTrades.OrderBy(t => t.EntryTimeUtc).ToArray();
        var stressByEntry = stressTrades
            .GroupBy(t => t.EntryTimeUtc)
            .ToDictionary(g => g.Key, g => g.First());

        var activatedCount = 0;
        var totalCheckpoints = 0;
        var activeRanges = new List<(DateTime Start, DateTime End)>();

        for (var checkpoint = studyStartUtc; checkpoint < studyEndUtc; checkpoint = checkpoint.AddHours(checkpointFrequencyHours))
        {
            totalCheckpoints++;
            var lookbackStart = checkpoint.AddDays(-lookbackDays);
            var lookbackModerate = moderateOrdered
                .Where(t => t.ExitTimeUtc < checkpoint && t.EntryTimeUtc >= lookbackStart)
                .ToArray();
            var lookbackStress = lookbackModerate
                .Select(t => stressByEntry.TryGetValue(t.EntryTimeUtc, out var s) ? s : t)
                .ToArray();

            var lookbackCount = lookbackModerate.Length;
            var lookbackNet = lookbackModerate.Sum(t => t.NetPnlQuote);
            var lookbackStressNet = lookbackStress.Sum(t => t.NetPnlQuote);

            var activated = lookbackCount >= minLookbackTrades;
            if (activated && requireNetPositive)
                activated = lookbackNet > 0m;
            if (activated && requireStressPlusPositive)
                activated = lookbackStressNet > 0m;

            var activationEnd = checkpoint.AddHours(activationPeriodHours);
            if (activationEnd > studyEndUtc)
                activationEnd = studyEndUtc;

            if (activated)
            {
                activatedCount++;
                activeRanges.Add((checkpoint, activationEnd));
            }
        }

        var merged = MergeRanges(activeRanges);
        var taken = moderateOrdered
            .Where(t => merged.Any(r => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End))
            .ToArray();

        return new DiagnosticSimOutcome(taken, activatedCount, totalCheckpoints);
    }

    private static List<(DateTime Start, DateTime End)> MergeRanges(
        IReadOnlyList<(DateTime Start, DateTime End)> ranges)
    {
        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var range in sorted)
        {
            if (merged.Count == 0 || range.Start > merged[^1].End)
                merged.Add(range);
            else if (range.End > merged[^1].End)
                merged[^1] = (merged[^1].Start, range.End);
        }

        return merged;
    }
}
