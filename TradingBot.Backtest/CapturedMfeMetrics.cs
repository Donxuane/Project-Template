namespace TradingBot.Backtest;

public sealed record CapturedMfeMetrics(
    string CalculationMode,
    decimal AvgCapturedMfePercentPositiveOnly,
    decimal? AvgCapturedMfeIncludingNegativeRatio,
    int NegativeCaptureTradeCount,
    decimal AvgCapturedMfeAllTradesWithMfe);

public static class CapturedMfeCalculator
{
    public const string CalculationMode = "ExitMoveOverMfeMoveRatio";

    public static CapturedMfeMetrics Compute(IReadOnlyList<SimulatedTrade> trades)
    {
        var withCapture = trades.Where(t => t.CapturedMfePercent.HasValue).ToArray();
        if (withCapture.Length == 0)
        {
            return new CapturedMfeMetrics(
                CalculationMode,
                AvgCapturedMfePercentPositiveOnly: 0m,
                AvgCapturedMfeIncludingNegativeRatio: null,
                NegativeCaptureTradeCount: 0,
                AvgCapturedMfeAllTradesWithMfe: 0m);
        }

        var positive = withCapture.Where(t => t.CapturedMfePercent!.Value >= 0m).ToArray();
        var negative = withCapture.Where(t => t.CapturedMfePercent!.Value < 0m).ToArray();

        return new CapturedMfeMetrics(
            CalculationMode,
            AvgCapturedMfePercentPositiveOnly: positive.Length == 0 ? 0m : positive.Average(t => t.CapturedMfePercent!.Value),
            AvgCapturedMfeIncludingNegativeRatio: withCapture.Average(t => t.CapturedMfePercent!.Value),
            NegativeCaptureTradeCount: negative.Length,
            AvgCapturedMfeAllTradesWithMfe: withCapture.Average(t => t.CapturedMfePercent!.Value));
    }
}
