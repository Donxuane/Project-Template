using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

/// <summary>
/// Reporting-only normalization of simulated 1-unit trade PnL to standard account sizes and leverage.
/// Does not modify strategy behavior, frozen configs, or activation thresholds.
/// </summary>
public static class NormalizedRiskPnlModule
{
  public const decimal AccountSize100Usdt = 100m;
  public const decimal AccountSize1000Usdt = 1000m;
  public const int Leverage3x = 3;
  public const int Leverage5x = 5;

  private static readonly JsonSerializerOptions ReadJsonOptions = new() { PropertyNameCaseInsensitive = true };

  public static decimal ReferenceUnitNotionalUsd(string symbol)
      => CrossSymbolCandidateEngineV2Catalog.ReferenceUnitNotionalUsd(symbol);

  public static NormalizedRiskPnlMetrics Compute(string symbol, decimal netPnlQuote, decimal maxDrawdownQuote = 0m)
  {
    var refNotional = ReferenceUnitNotionalUsd(symbol);
    if (refNotional <= 0m)
      return new NormalizedRiskPnlMetrics();

    var scale100x3 = FractionalScale(refNotional, AccountSize100Usdt, Leverage3x);
    var scale100x5 = FractionalScale(refNotional, AccountSize100Usdt, Leverage5x);
    var scale1000x3 = FractionalScale(refNotional, AccountSize1000Usdt, Leverage3x);
    var scale1000x5 = FractionalScale(refNotional, AccountSize1000Usdt, Leverage5x);

    var scaledDd100x3 = maxDrawdownQuote * scale100x3;
    var scaledDd1000x3 = maxDrawdownQuote * scale1000x3;

    return new NormalizedRiskPnlMetrics
    {
      AssumedUnitNotionalUsdt = refNotional,
      FractionalPositionScaleAt100Usdt3x = scale100x3,
      FractionalPositionScaleAt100Usdt5x = scale100x5,
      FractionalPositionScaleAt1000Usdt3x = scale1000x3,
      FractionalPositionScaleAt1000Usdt5x = scale1000x5,
      NetPnlPer100UsdtAt3x = ScaleNet(netPnlQuote, scale100x3),
      NetPnlPer100UsdtAt5x = ScaleNet(netPnlQuote, scale100x5),
      NetPnlPer1000UsdtAt3x = ScaleNet(netPnlQuote, scale1000x3),
      NetPnlPer1000UsdtAt5x = ScaleNet(netPnlQuote, scale1000x5),
      RequiredMarginUsdtAt3x = RequiredMargin(refNotional, scale100x3, Leverage3x, AccountSize100Usdt),
      RequiredMarginUsdtAt5x = RequiredMargin(refNotional, scale100x5, Leverage5x, AccountSize100Usdt),
      MaxDrawdownPercentOf100Usdt = AccountSize100Usdt > 0m
          ? Math.Round(scaledDd100x3 / AccountSize100Usdt * 100m, 4)
          : 0m,
      MaxDrawdownPercentOf1000Usdt = AccountSize1000Usdt > 0m
          ? Math.Round(scaledDd1000x3 / AccountSize1000Usdt * 100m, 4)
          : 0m
    };
  }

  public static decimal ComputeMaxDrawdownFromNetSeries(IEnumerable<decimal> orderedNetPnl)
  {
    decimal equity = 0m, peak = 0m, maxDd = 0m;
    foreach (var net in orderedNetPnl)
    {
      equity += net;
      if (equity > peak) peak = equity;
      var dd = peak - equity;
      if (dd > maxDd) maxDd = dd;
    }

    return Math.Round(maxDd, 8);
  }

  public static NormalizedCrossSymbolTradeReportRow ToReportRow(CrossSymbolTradeRow trade)
  {
    var risk = Compute(trade.Symbol, trade.NetPnlQuote);
    return new NormalizedCrossSymbolTradeReportRow
    {
      Symbol = trade.Symbol,
      Interval = trade.Interval,
      Direction = trade.Direction,
      TargetPercent = trade.TargetPercent,
      StopPercent = trade.StopPercent,
      ActivationRule = trade.ActivationRule,
      EntryTimeUtc = trade.EntryTimeUtc,
      ExitTimeUtc = trade.ExitTimeUtc,
      NetPnlQuote = trade.NetPnlQuote,
      IsWinner = trade.IsWinner,
      ExitReason = trade.ExitReason,
      CostScenario = trade.CostScenario,
      NormalizedRisk = risk
    };
  }

  public static NormalizedShortWindowTradeReportRow ToReportRow(ShortWindowTradeRow trade, string symbol = "BNBUSDT")
  {
    var risk = Compute(symbol, trade.NetPnlQuote);
    return new NormalizedShortWindowTradeReportRow
    {
      ActivationRuleName = trade.ActivationRuleName,
      EntryTimeUtc = trade.EntryTimeUtc,
      ExitTimeUtc = trade.ExitTimeUtc,
      NetPnlQuote = trade.NetPnlQuote,
      IsWinner = trade.IsWinner,
      ExitReason = trade.ExitReason,
      CostScenario = trade.CostScenario,
      ActivationStartUtc = trade.ActivationStartUtc,
      ActivationEndUtc = trade.ActivationEndUtc,
      SparseLookbackActivation = trade.SparseLookbackActivation,
      NormalizedRisk = risk
    };
  }

  public static string BuildForwardIncubationCompactSummaryLine(
      string currentTrack,
      decimal currentNet,
      string currentSymbol,
      decimal maxDrawdown,
      string verdict,
      string nextAction,
      string outputDirectory)
  {
    var bnb5Net = currentTrack is "BNB" or "BNB5"
        ? currentNet
        : TryReadSiblingForwardNet(outputDirectory, "BNB") ?? 0m;
    var sol5Net = currentTrack is "SOL" or "SOL5"
        ? currentNet
        : TryReadSiblingForwardNet(outputDirectory, "SOL") ?? 0m;
    var eth15Net = currentTrack is "ETH15"
        ? currentNet
        : TryReadEth15ForwardNet(outputDirectory);

    var bnb5Label = currentTrack is "BNB" or "BNB5" || TryReadSiblingForwardNet(outputDirectory, "BNB").HasValue
        ? bnb5Net.ToString("F2", CultureInfo.InvariantCulture)
        : "n/a";
    var sol5Label = currentTrack is "SOL" or "SOL5" || TryReadSiblingForwardNet(outputDirectory, "SOL").HasValue
        ? sol5Net.ToString("F2", CultureInfo.InvariantCulture)
        : "n/a";
    var eth15Label = eth15Net.HasValue
        ? eth15Net.Value.ToString("F2", CultureInfo.InvariantCulture)
        : "n/a";

    var normalized = Compute(currentSymbol, currentNet, maxDrawdown);
    var est = normalized.NetPnlPer100UsdtAt3x.ToString("F2", CultureInfo.InvariantCulture);

    return $"Overall net: BNB5 {bnb5Label}; SOL5 {sol5Label}; ETH15 {eth15Label} | Normalized Est. (per $100 at 3x): {est} | Verdict: {verdict} | Next action: {nextAction}";
  }

  public static string SummaryCsvHeaderSuffix()
      => "AssumedUnitNotionalUsdt,NetPnlPer100UsdtAt3x,NetPnlPer100UsdtAt5x,NetPnlPer1000UsdtAt3x,NetPnlPer1000UsdtAt5x,RequiredMarginUsdtAt3x,RequiredMarginUsdtAt5x,MaxDrawdownPercentOf100Usdt,MaxDrawdownPercentOf1000Usdt";

  public static string SummaryCsvValues(NormalizedRiskPnlMetrics? m)
  {
    if (m is null)
      return ",,,,,,,";
    return string.Join(",",
        m.AssumedUnitNotionalUsdt,
        m.NetPnlPer100UsdtAt3x,
        m.NetPnlPer100UsdtAt5x,
        m.NetPnlPer1000UsdtAt3x,
        m.NetPnlPer1000UsdtAt5x,
        m.RequiredMarginUsdtAt3x,
        m.RequiredMarginUsdtAt5x,
        m.MaxDrawdownPercentOf100Usdt,
        m.MaxDrawdownPercentOf1000Usdt);
  }

  public static string TradeCsvHeaderSuffix()
      => SummaryCsvHeaderSuffix() + ",FractionalPositionScaleAt100Usdt3x,FractionalPositionScaleAt100Usdt5x";

  public static string TradeCsvValues(NormalizedRiskPnlMetrics m)
      => SummaryCsvValues(m) + "," + m.FractionalPositionScaleAt100Usdt3x + "," + m.FractionalPositionScaleAt100Usdt5x;

  public static void AppendSummaryRiskLines(StringBuilder sb, NormalizedRiskPnlMetrics m)
  {
    sb.AppendLine($"AssumedUnitNotionalUsdt: {m.AssumedUnitNotionalUsdt}");
    sb.AppendLine($"NetPnlPer100UsdtAt3x: {m.NetPnlPer100UsdtAt3x} | NetPnlPer100UsdtAt5x: {m.NetPnlPer100UsdtAt5x}");
    sb.AppendLine($"NetPnlPer1000UsdtAt3x: {m.NetPnlPer1000UsdtAt3x} | NetPnlPer1000UsdtAt5x: {m.NetPnlPer1000UsdtAt5x}");
    sb.AppendLine($"RequiredMarginUsdtAt3x: {m.RequiredMarginUsdtAt3x} | RequiredMarginUsdtAt5x: {m.RequiredMarginUsdtAt5x}");
    sb.AppendLine($"MaxDrawdownPercentOf100Usdt: {m.MaxDrawdownPercentOf100Usdt}% | MaxDrawdownPercentOf1000Usdt: {m.MaxDrawdownPercentOf1000Usdt}%");
  }

  private static decimal FractionalScale(decimal refNotional, decimal accountUsdt, int leverage)
  {
    var deployable = accountUsdt * leverage;
    return refNotional <= 0m ? 0m : Math.Round(Math.Min(1m, deployable / refNotional), 6);
  }

  private static decimal ScaleNet(decimal netPnlQuote, decimal fractionalScale)
      => Math.Round(netPnlQuote * fractionalScale, 8);

  private static decimal RequiredMargin(decimal refNotional, decimal fractionalScale, int leverage, decimal accountCapUsdt)
  {
    if (leverage <= 0)
      return 0m;
    var effectiveNotional = refNotional * fractionalScale;
    var margin = effectiveNotional / leverage;
    return Math.Round(Math.Min(margin, accountCapUsdt), 4);
  }

  private static decimal? TryReadSiblingForwardNet(string outputDirectory, string track)
  {
    var parent = Path.GetDirectoryName(Path.GetFullPath(outputDirectory));
    if (parent is null)
      return null;

    var (siblingDir, summaryFile) = track switch
    {
      "BNB" => (
          Path.Combine(parent, "no-paid-short-window-forward-incubation-v1-run"),
          "frozen-candidate-summary.json"),
      "SOL" => (
          Path.Combine(parent, "no-paid-short-window-sol-forward-incubation-v1"),
          "frozen-sol-candidate-summary.json"),
      _ => (string.Empty, string.Empty)
    };

    if (string.IsNullOrEmpty(siblingDir))
      return null;

    return TryReadForwardNetModerate(Path.Combine(siblingDir, summaryFile));
  }

  private static decimal? TryReadEth15ForwardNet(string outputDirectory)
  {
    var parent = Path.GetDirectoryName(Path.GetFullPath(outputDirectory));
    if (parent is null)
      return null;

    var summaryPath = Path.Combine(parent, "fixed-frequency-eth15-forward-incubation-v1", "forward-incubation-summary.json");
    if (!File.Exists(summaryPath))
      return null;

    try
    {
      using var stream = File.OpenRead(summaryPath);
      var summary = JsonSerializer.Deserialize<FixedFrequencyForwardIncubationSummary>(stream, ReadJsonOptions);
      return summary?.NetModerate;
    }
    catch
    {
      return null;
    }
  }

  private static decimal? TryReadForwardNetModerate(string summaryPath)
  {
    if (!File.Exists(summaryPath))
      return null;

    try
    {
      using var stream = File.OpenRead(summaryPath);
      var row = JsonSerializer.Deserialize<FrozenCandidateSummaryRow>(stream, ReadJsonOptions);
      return row?.ForwardNetModerate;
    }
    catch
    {
      return null;
    }
  }
}
