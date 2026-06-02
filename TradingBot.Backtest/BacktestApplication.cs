using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingBot.Application.DecisionEngine;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;

namespace TradingBot.Backtest;

public sealed class BacktestApplication(BacktestSettings settings)
{
    public async Task<BacktestRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var allSummaries = new List<ReplaySummaryRow>();
        var allTrades = new List<SimulatedTrade>();
        var allBlockedEntries = new List<BlockedEntryRecord>();
        var aggregationDiagnostics = new List<AggregationDiagnosticsRecord>();
        var allIssues = new List<DataQualityIssue>();

        var dataLoader = new HistoricalKlineDataLoader(settings);
        var isMultiIntervalRun = settings.Intervals.Count > 1;

        var profiles = BuildDefaultProfiles();
        var allSymbols = profiles.SelectMany(p => p.Symbols).Distinct().ToArray();
        var validatedDataBySymbol = new Dictionary<TradingSymbol, SymbolValidationResult>();
        foreach (var symbol in allSymbols)
        {
            var validation = await dataLoader.LoadAndValidateAsync(symbol, cancellationToken);
            validatedDataBySymbol[symbol] = validation;
            allIssues.AddRange(validation.Issues);
        }

        foreach (var interval in settings.Intervals)
        {
            var intervalSummaries = new List<ReplaySummaryRow>();
            var intervalTrades = new List<SimulatedTrade>();
            var intervalBlockedEntries = new List<BlockedEntryRecord>();
            foreach (var profile in profiles)
            {
                StrategyStaticStateResetter.ResetMovingAverageTrendStrategyState();

                var configuration = BuildConfiguration(profile.ConfigOverrides);
                var trendSettings = new TrendStateSettings
                {
                    MinTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:MinTrendStrengthPercent") ?? 0.001m),
                    StrongTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:StrongTrendStrengthPercent") ?? 0.003m),
                    MinSlopePercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:MinSlopePercent") ?? 0.0005m),
                    LowVolatilityRangePercentThreshold = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:LowVolatilityRangePercentThreshold") ?? 0.0008m),
                    MinRangeCandlesForVolatilityCheck = Math.Max(2, configuration.GetValue<int?>("DecisionEngine:TrendState:MinRangeCandlesForVolatilityCheck") ?? 10)
                };

                ITrendStateService trendService = new TrendStateService(
                    NullLogger<TrendStateService>.Instance,
                    Options.Create(trendSettings));
                IVolatilityService volatilityService = new VolatilityService(configuration);
                IAtrService atrService = new AtrService(configuration);
                IMarketConditionService marketConditionService = new MarketConditionService(configuration, volatilityService, atrService);
                var positionManager = new PositionManager();
                var marketStateTracker = new MarketStateTracker();

                var strategy = new MovingAverageTrendStrategy(
                    NullLogger<MovingAverageTrendStrategy>.Instance,
                    trendService,
                    positionManager,
                    marketStateTracker,
                    marketConditionService,
                    configuration);

                var profileSymbols = string.Join("+", profile.Symbols.Select(s => s.ToString()));
                var simulator = new ExecutionSimulator(new ExecutionCostSettings(
                    settings.FeeRatePercent,
                    settings.EstimatedSpreadPercent,
                    settings.SlippagePercent));
                var guard = new BacktestEntryGuard(configuration, new ExecutionCostSettings(
                    settings.FeeRatePercent,
                    settings.EstimatedSpreadPercent,
                    settings.SlippagePercent));
                var signalStats = new ProfileSignalStats();
                var runtimeSnapshot = ReadProfileRuntimeSnapshot(configuration);

                var profileTrades = new List<SimulatedTrade>();
                foreach (var symbol in profile.Symbols)
                {
                    var validation = validatedDataBySymbol[symbol];
                    if (validation.Candles.Count == 0)
                        continue;

                    var aggregate = CandleAggregator.Aggregate(symbol, validation.Candles, "1m", interval);
                    var inheritedGapCount = CountGapWarnings(validation.Issues);
                    aggregationDiagnostics.Add(new AggregationDiagnosticsRecord
                    {
                        Interval = interval,
                        SourceInterval = "1m",
                        TargetInterval = interval,
                        Symbol = symbol,
                        InputCandleCount = aggregate.InputCandleCount,
                        OutputCandleCount = aggregate.OutputCandleCount,
                        DroppedIncompleteFinalBucketCount = aggregate.DroppedIncompleteFinalBucketCount,
                        InheritedGapCount = inheritedGapCount
                    });
                    allIssues.Add(new DataQualityIssue(
                        interval,
                        symbol,
                        "info",
                        $"Aggregation diagnostics: source=1m,target={interval},input={aggregate.InputCandleCount},output={aggregate.OutputCandleCount},droppedIncompleteFinalBucketCount={aggregate.DroppedIncompleteFinalBucketCount},inheritedGapCount={inheritedGapCount}."));

                    if (aggregate.Candles.Count == 0)
                        continue;

                    var quantity = ResolveQuantity(configuration, symbol);
                    var replayTrades = RunSymbolReplay(
                        interval,
                        profile.ProfileName,
                        profileSymbols,
                        strategy,
                        symbol,
                        aggregate.Candles,
                        quantity,
                        settings.ForceCloseAtEnd,
                        guard,
                        simulator,
                        signalStats,
                        intervalBlockedEntries,
                        cancellationToken);
                    profileTrades.AddRange(replayTrades);
                }

                intervalTrades.AddRange(profileTrades);
                intervalSummaries.Add(ReplaySummaryAggregator.BuildSummary(
                    interval,
                    profile.ProfileName,
                    profileSymbols,
                    profileTrades,
                    signalStats,
                    runtimeSnapshot));
            }

            var intervalOutputDirectory = ResolveIntervalOutputDirectory(settings.OutputDirectory, interval, isMultiIntervalRun);
            var reportWriter = new ReplayReportWriter(intervalOutputDirectory);
            var intervalIssues = allIssues.Where(x => x.Interval == "1m" || x.Interval == interval).ToArray();
            await reportWriter.WriteAsync(intervalSummaries, intervalTrades, intervalBlockedEntries, intervalIssues, cancellationToken);

            allSummaries.AddRange(intervalSummaries);
            allTrades.AddRange(intervalTrades);
            allBlockedEntries.AddRange(intervalBlockedEntries);
        }

        if (isMultiIntervalRun)
        {
            var crossIntervalReportWriter = new ReplayReportWriter(settings.OutputDirectory);
            await crossIntervalReportWriter.WriteCrossIntervalSummaryAsync(allSummaries, cancellationToken);
            await crossIntervalReportWriter.WriteAggregationDiagnosticsAsync(aggregationDiagnostics, cancellationToken);
        }

        return new BacktestRunResult(allSummaries, allTrades, allBlockedEntries, aggregationDiagnostics, allIssues);
    }

    private IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string> overrides)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(settings.AppSettingsPath, optional: false);

        if (overrides.Count > 0)
            builder.AddInMemoryCollection(overrides.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));

        return builder.Build();
    }

    private static decimal ResolveQuantity(IConfiguration configuration, TradingSymbol symbol)
    {
        var symbolQty = configuration.GetValue<decimal?>($"DecisionEngine:SymbolQuantities:{symbol}");
        if (symbolQty.HasValue && symbolQty.Value > 0m)
            return symbolQty.Value;

        var fallback = configuration.GetValue<decimal?>("DecisionEngine:Quantity") ?? 0.001m;
        return Math.Max(0.00000001m, fallback);
    }

    private static IReadOnlyList<SimulatedTrade> RunSymbolReplay(
        string interval,
        string profileName,
        string symbolsText,
        MovingAverageTrendStrategy strategy,
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> candles,
        decimal quantity,
        bool forceCloseAtEnd,
        BacktestEntryGuard guard,
        ExecutionSimulator simulator,
        ProfileSignalStats signalStats,
        List<BlockedEntryRecord> blockedEntriesDestination,
        CancellationToken cancellationToken)
    {
        var trades = new List<SimulatedTrade>();
        var required = Math.Max(2, strategy.RequiredPeriods);
        StrategyEvaluation? lastEvaluation = null;

        for (var i = 0; i < candles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i + 1 < required)
                continue;

            var snapshot = MarketSnapshotFactory.Build(candles, i);
            var signal = strategy.GenerateSignalAsync(snapshot, cancellationToken, allowStateMutation: true)
                .GetAwaiter()
                .GetResult();
            lastEvaluation = new StrategyEvaluation(signal, snapshot);

            if (signal.Signal == TradeSignal.Buy)
            {
                signalStats.RawBuySignals++;
                var decision = guard.Evaluate(symbol, signal, snapshot, simulator.HasOpenPosition(symbol));
                if (!decision.IsAllowed)
                {
                    signalStats.IncrementBlocked(decision.Reason);
                    blockedEntriesDestination.Add(new BlockedEntryRecord
                    {
                        Interval = interval,
                        ProfileName = profileName,
                        Symbols = symbolsText,
                        Symbol = symbol,
                        TimeUtc = snapshot.TimestampUtc,
                        Reason = decision.Reason,
                        Confidence = signal.Confidence,
                        ConfidenceThreshold = decision.ConfidenceThreshold,
                        ExpectedMovePercent = signal.ExpectedMovePercent,
                        EstimatedNetMovePercent = decision.EstimatedNetMovePercent,
                        ExpectedTargetSource = signal.ExpectedTargetSource,
                        SignalReason = signal.Reason
                    });
                    continue;
                }

                signalStats.ExecutedBuySignals++;
                simulator.OnSignal(
                    interval,
                    symbol,
                    quantity,
                    signal,
                    snapshot,
                    profileName,
                    symbolsText,
                    trades,
                    wasGuarded: true,
                    estimatedRoundTripCostPercent: decision.EstimatedRoundTripCostPercent,
                    estimatedNetMovePercent: decision.EstimatedNetMovePercent);
                continue;
            }

            simulator.OnSignal(interval, symbol, quantity, signal, snapshot, profileName, symbolsText, trades);
        }

        if (forceCloseAtEnd && lastEvaluation is not null)
        {
            simulator.ForceClose(symbol, lastEvaluation.Snapshot, "EndOfData", profileName, symbolsText, trades);
        }

        return trades;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildDefaultProfiles()
    {
        var strategyVariants = new[]
        {
            new
            {
                Name = "current-guarded-baseline",
                Overrides = new Dictionary<string, string>()
            },
            new
            {
                Name = "lowvol-disabled",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.00",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true"
                }
            },
            new
            {
                Name = "lowvol-strict-confirmed",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles"] = "20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutBufferPercent"] = "0.0010",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutConfirmationCandles"] = "2",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:RequireNoImmediateBearishCandleAfterBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:MinBreakoutSlopePercent"] = "0.0004"
                }
            },
            new
            {
                Name = "lowvol-very-strict",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles"] = "24",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutBufferPercent"] = "0.0020",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutConfirmationCandles"] = "2",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:RequireNoImmediateBearishCandleAfterBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:MinBreakoutSlopePercent"] = "0.0008"
                }
            }
        };

        var symbolSets = new[]
        {
            new[] { TradingSymbol.ETHUSDT },
            new[] { TradingSymbol.BNBUSDT },
            new[] { TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT },
            new[] { TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT }
        };

        var profiles = new List<ReplayProfileDefinition>();
        foreach (var variant in strategyVariants)
        {
            foreach (var symbols in symbolSets)
            {
                var symbolLabel = string.Join("+", symbols.Select(s => s.ToString().Replace("USDT", string.Empty, StringComparison.OrdinalIgnoreCase)));
                profiles.Add(new ReplayProfileDefinition(
                    $"{variant.Name}-{symbolLabel}",
                    symbols,
                    variant.Overrides));
            }
        }

        return profiles;
    }

    private static ProfileRuntimeSnapshot ReadProfileRuntimeSnapshot(IConfiguration configuration)
    {
        return new ProfileRuntimeSnapshot(
            EnableLowVolatilityBreakoutEntry: configuration.GetValue<bool?>("DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry") ?? true,
            BreakoutLookbackCandles: Math.Max(2, configuration.GetValue<int?>("DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles") ?? 10),
            BreakoutBufferPercent: Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:MovingAverageCrossoverStrategy:BreakoutBufferPercent") ?? 0m),
            BreakoutConfirmationCandles: Math.Max(1, configuration.GetValue<int?>("DecisionEngine:MovingAverageCrossoverStrategy:BreakoutConfirmationCandles") ?? 1),
            MinBreakoutSlopePercent: Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:MovingAverageCrossoverStrategy:MinBreakoutSlopePercent") ?? 0m),
            UseConfirmedClosedCandlesForLowVolBreakout: configuration.GetValue<bool?>("DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout") ?? false);
    }

    public static string ResolveIntervalOutputDirectory(string baseOutputDirectory, string interval, bool multiIntervalRun)
        => multiIntervalRun ? Path.Combine(baseOutputDirectory, interval) : baseOutputDirectory;

    private static int CountGapWarnings(IReadOnlyList<DataQualityIssue> issues)
        => issues.Count(x => x.Message.Contains("Gap detected:", StringComparison.OrdinalIgnoreCase));
}
