using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Binance Futures testnet shadow runner — engineering preparation only.
/// Evaluates frozen incubation profiles at the latest data timestamp and emits
/// would-place-order records. Never places real orders by default.
/// </summary>
public sealed class FuturesTestnetShadowApplication(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FuturesTestnetShadowRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settings.AppSettingsPath, optional: false, reloadOnChange: false)
            .Build();
        var shadowSettings = FuturesTestnetShadowSettings.Load(configuration);
        var keySafety = FuturesTestnetShadowKeySafety.Evaluate(settings.AppSettingsPath, shadowSettings);

        Directory.CreateDirectory(settings.OutputDirectory);
        if (keySafety.BlockShadowRun)
        {
            var blocked = BuildBlockedResult(runAtUtc, shadowSettings, keySafety);
            await new FuturesTestnetShadowReportWriter(settings.OutputDirectory).WriteAsync(blocked, shadowSettings, cancellationToken);
            await WriteRunMetadataAsync(blocked, shadowSettings, cancellationToken);
            return blocked;
        }

        var loader = new HistoricalKlineDataLoader(settings);
        var downloadOutcomes = new List<ShortWindowDownloadOutcome>();
        if (settings.BootstrapFuturesData)
        {
            await RefreshCandlesAsync(loader, cancellationToken);
            var flowDownloader = new ShortWindowFlowDataDownloader();
            downloadOutcomes.AddRange(await flowDownloader.DownloadAllAsync(
                settings.DataDirectory, FuturesMarketDataCatalog.Symbols, runAtUtc.AddDays(-365), runAtUtc, cancellationToken));
        }

        var decisions = new List<FuturesTestnetShadowDecisionRow>();
        var riskRows = new List<FuturesTestnetShadowRiskRow>();

        foreach (var profile in FuturesTestnetShadowCatalog.Profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = await EvaluateProfileAsync(
                profile, loader, shadowSettings, runAtUtc, cancellationToken);
            decisions.Add(decision);
            riskRows.Add(BuildRiskRow(profile, decision, shadowSettings));
        }

        var summary = BuildSummary(runAtUtc, decisions, shadowSettings, keySafety.Status);
        var result = new FuturesTestnetShadowRunResult(
            summary,
            decisions,
            riskRows,
            keySafety.Status,
            SafetyBlockedRealKeys: false,
            downloadOutcomes,
            settings.BootstrapFuturesData);

        await new FuturesTestnetShadowReportWriter(settings.OutputDirectory).WriteAsync(result, shadowSettings, cancellationToken);
        await WriteRunMetadataAsync(result, shadowSettings, cancellationToken);
        return result;
    }

    private async Task<FuturesTestnetShadowDecisionRow> EvaluateProfileAsync(
        FuturesTestnetShadowCatalog.ProfileRef profile,
        HistoricalKlineDataLoader loader,
        FuturesTestnetShadowSettings shadowSettings,
        DateTime runAtUtc,
        CancellationToken cancellationToken)
    {
        var frozenPath = profile.FrozenStatePath(settings.DataDirectory);
        if (!File.Exists(frozenPath))
        {
            return BlockedDecision(profile, runAtUtc, "FrozenProfileMissing",
                $"Frozen state not found: {frozenPath}", shadowSettings, settings.DataDirectory);
        }

        var state = JsonSerializer.Deserialize<FrozenCandidateState>(await File.ReadAllTextAsync(frozenPath, cancellationToken))
                    ?? throw new InvalidOperationException($"Could not deserialize frozen state: {frozenPath}");

        var symbolData = await loader.LoadAndValidateAsync(profile.Symbol, cancellationToken);
        var btc = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (symbolData.Candles.Count == 0 || btc.Candles.Count == 0)
            return BlockedDecision(profile, runAtUtc, "MissingLocalCandles", "Symbol or BTCUSDT local candle data missing.", shadowSettings, settings.DataDirectory);

        var validated = new Dictionary<TradingSymbol, SymbolValidationResult>
        {
            [profile.Symbol] = symbolData,
            [TradingSymbol.BTCUSDT] = btc
        };
        var (_, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validated);
        var evalUtc = dataEndUtc;
        var spanDays = (int)Math.Max(1, (dataEndUtc - symbolData.Candles[0].OpenTimeUtc).TotalDays);
        var windowStart = dataEndUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < symbolData.Candles[0].OpenTimeUtc)
            windowStart = symbolData.Candles[0].OpenTimeUtc;

        var windowSymbol = CandleWindowSlicer.Slice(symbolData.Candles, windowStart, dataEndUtc);
        var windowBtc = CandleWindowSlicer.Slice(btc.Candles, windowStart, dataEndUtc);
        var intervalCandles = CandleAggregator.Aggregate(profile.Symbol, windowSymbol, "1m", profile.Interval).Candles;
        var btcContext = new BtcContextIndex(windowBtc);
        var marketWideContext = new MarketWideContextIndex(
            new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>
            {
                [profile.Symbol] = windowSymbol,
                [TradingSymbol.BTCUSDT] = windowBtc
            },
            includeBtcInProxy: true);
        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);
        var flowIndex = new ShortWindowFlowFeatureIndex(futuresLoader, profile.Symbol, intervalCandles, windowBtc);
        var frozenStart = state.FrozenStartUtc;

        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades;
        LongShortDirection direction;
        decimal targetPercent;
        decimal stopPercent;
        int holdMinutes;
        FuturesTestnetShadowEvaluator.ActivationState activation;

        if (profile.IsBnbRule01)
        {
            var discoveryPath = ResolveDiscoveryJsonPath();
            var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
            var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("Rule01 not found.");
            var simProfile = NoPaidDataShortWindowFlowResearchV1Catalog.BuildProfile(rule);
            var v2Profile = DirectionalRuleFuturesValidationV31Catalog.ToV2Profile(simProfile);
            var scan = DirectionalRuleFuturesValidationV2Simulator.ScanProfile(
                v2Profile, "365d", intervalCandles, windowSymbol, btcContext, marketWideContext, cancellationToken);
            baseTrades = scan.Trades;
            direction = LongShortDirection.Short;
            targetPercent = state.TargetPercent;
            stopPercent = state.StopPercent;
            holdMinutes = state.MaxHoldMinutes;

            var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                baseTrades, profile.PrimaryCostScenario, btcContext, dataEndUtc);
            var shortConfig = profile.BuildShortWindowActivationConfig();
            activation = FuturesTestnetShadowEvaluator.EvaluateShortWindowActivation(
                shortConfig, moderateTrades, evalUtc, frozenStart, flowIndex);
        }
        else
        {
            var key = profile.ComboKey!;
            var scans = NoPaidDataShortWindowFlowResearchV1CrossSymbolSimulator.ScanSymbolInterval(
                profile.Symbol, profile.Interval, intervalCandles, windowSymbol, flowIndex, btcContext,
                marketWideContext, windowStart, dataEndUtc, cancellationToken);
            var scan = scans.First(s => s.Key == key);
            baseTrades = scan.BaseTrades;
            direction = key.Direction;
            targetPercent = key.TargetPercent;
            stopPercent = key.StopPercent;
            holdMinutes = key.MaxHoldMinutes;

            var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                baseTrades, profile.PrimaryCostScenario, btcContext, dataEndUtc);
            var crossConfig = profile.BuildCrossSymbolActivationConfig();
            activation = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolActivation(
                crossConfig, key, moderateTrades, evalUtc, frozenStart, flowIndex);
        }

        var cooldown = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.CooldownFor(profile.Interval);
        var entry = profile.IsBnbRule01
            ? FuturesTestnetShadowEvaluator.EvaluateBnbRule01EntryNow(baseTrades, evalUtc, frozenStart, profile.Interval == "15m" ? 15 : 5)
            : FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
                profile.ComboKey!, intervalCandles, baseTrades, evalUtc, frozenStart, cooldown);

        var filters = await FuturesTestnetShadowExchangeInfo.GetFiltersAsync(profile.Symbol.ToString(), cancellationToken);
        var notional = Math.Min(shadowSettings.MaxNotionalUsdt, shadowSettings.MaxNotionalUsdt);
        var entryPrice = entry.EntryPrice ?? (intervalCandles.Count > 0 ? intervalCandles[^1].Close : 0m);
        var quantityRaw = entryPrice > 0m ? notional / entryPrice : 0m;
        var (quantityRounded, precisionValid) = FuturesTestnetShadowExchangeInfo.RoundQuantity(quantityRaw, filters);
        var roundedNotional = quantityRounded * entryPrice;
        var withinMaxNotional = roundedNotional <= shadowSettings.MaxNotionalUsdt && roundedNotional > 0m;

        decimal? targetPrice = null;
        decimal? stopPrice = null;
        if (entryPrice > 0m)
        {
            if (direction == LongShortDirection.Short)
            {
                targetPrice = Math.Round(entryPrice * (1m - targetPercent / 100m), 8);
                stopPrice = Math.Round(entryPrice * (1m + stopPercent / 100m), 8);
            }
            else
            {
                targetPrice = Math.Round(entryPrice * (1m + targetPercent / 100m), 8);
                stopPrice = Math.Round(entryPrice * (1m - stopPercent / 100m), 8);
            }
        }

        var incubationOutputRoot = FuturesTestnetShadowForwardEvidenceLoader.IncubationOutputRootFromDataDirectory(settings.DataDirectory);
        var forwardEvidence = FuturesTestnetShadowForwardEvidenceLoader.Load(
            profile, settings.DataDirectory, incubationOutputRoot, state.ProfileName);
        var forwardEvidencePassed = FuturesTestnetShadowForwardEvidenceLoader.ComputeForwardEvidencePassed(
            forwardEvidence, shadowSettings.RequireForwardTradeEvidence);
        var blocks = new List<string>();
        if (!activation.Passed)
            blocks.Add("ActivationNotPassed");
        if (!entry.Present)
            blocks.Add("NoEntrySignal");
        FuturesTestnetShadowForwardEvidenceLoader.ApplyForwardEvidenceBlocks(
            forwardEvidence, shadowSettings.RequireForwardTradeEvidence, blocks);
        if (!withinMaxNotional)
            blocks.Add("NotionalOutOfRange");
        if (!precisionValid)
            blocks.Add("PrecisionInvalid");

        // WouldPlaceOrder is shadow intent only; DryRunOnly / AllowTestnetOrders gate actual API calls, not this flag.
        var wouldPlace = blocks.Count == 0;
        var shadowRunnerCanPlaceIfSignalAppears = activation.Passed
            && forwardEvidencePassed
            && withinMaxNotional
            && precisionValid;
        var orderSide = direction == LongShortDirection.Short ? "SELL" : "BUY";
        var margin = shadowSettings.Leverage > 0
            ? Math.Round(roundedNotional / shadowSettings.Leverage, 4)
            : roundedNotional;

        return new FuturesTestnetShadowDecisionRow
        {
            TimestampUtc = evalUtc,
            ProfileName = profile.ProfileName,
            Symbol = profile.Symbol.ToString(),
            Interval = profile.Interval,
            Direction = direction.ToString(),
            ActivationPassed = activation.Passed,
            ActivationReason = activation.Reason,
            EntrySignalPresent = entry.Present,
            EntryReason = entry.Reason,
            WouldPlaceOrder = wouldPlace,
            OrderSide = entry.Present ? orderSide : string.Empty,
            IntendedEntryPrice = entryPrice > 0m ? entryPrice : null,
            TargetPrice = targetPrice,
            StopPrice = stopPrice,
            HoldHours = Math.Round(holdMinutes / 60m, 4),
            AssumedNotionalUsdt = roundedNotional,
            NetPnlPer100Usdt = FuturesTestnetShadowEvaluator.EstimateNetPnlPer100Usdt(targetPercent, stopPercent, direction),
            RequiredMarginAtLeverage = margin,
            Leverage = shadowSettings.Leverage,
            QuantityRaw = quantityRaw,
            QuantityRounded = quantityRounded,
            PriceTickSize = filters.TickSize,
            QuantityStepSize = filters.StepSize,
            MinNotional = filters.MinNotional,
            PrecisionValid = precisionValid && withinMaxNotional,
            RiskStatus = wouldPlace ? "WouldPlaceShadowOrder" : "Blocked",
            ReasonIfBlocked = wouldPlace ? string.Empty : string.Join("; ", blocks),
            RequireForwardTradeEvidence = shadowSettings.RequireForwardTradeEvidence,
            ForwardTradeCount = forwardEvidence.ForwardTradeCount,
            ForwardNetModerate = forwardEvidence.ForwardNetModerate,
            ForwardNetStressPlus = forwardEvidence.ForwardNetStressPlus,
            ForwardEvidencePassed = forwardEvidencePassed,
            ShadowRunnerCanPlaceIfSignalAppears = shadowRunnerCanPlaceIfSignalAppears,
            ForwardEvidenceSourceFile = forwardEvidence.ForwardEvidenceSourceFile,
            ForwardEvidenceSourceProfileName = forwardEvidence.ForwardEvidenceSourceProfileName,
            ForwardEvidenceWindowStartUtc = forwardEvidence.WindowStartUtc,
            ForwardEvidenceWindowEndUtc = forwardEvidence.WindowEndUtc,
            ForwardEvidenceIsTrueForwardOnly = forwardEvidence.ForwardEvidenceIsTrueForwardOnly
        };
    }

    private static FuturesTestnetShadowRiskRow BuildRiskRow(
        FuturesTestnetShadowCatalog.ProfileRef profile,
        FuturesTestnetShadowDecisionRow decision,
        FuturesTestnetShadowSettings shadowSettings)
        => new()
        {
            ProfileName = profile.ProfileName,
            Symbol = decision.Symbol,
            AssumedNotionalUsdt = decision.AssumedNotionalUsdt,
            Leverage = shadowSettings.Leverage,
            RequiredMarginAtLeverage = decision.RequiredMarginAtLeverage,
            MaxNotionalUsdtLimit = shadowSettings.MaxNotionalUsdt,
            WithinMaxNotional = decision.AssumedNotionalUsdt > 0m && decision.AssumedNotionalUsdt <= shadowSettings.MaxNotionalUsdt,
            PrecisionValid = decision.PrecisionValid,
            RiskStatus = decision.RiskStatus,
            ReasonIfBlocked = decision.ReasonIfBlocked
        };

    private static FuturesTestnetShadowSummaryRow BuildSummary(
        DateTime runAtUtc,
        IReadOnlyList<FuturesTestnetShadowDecisionRow> decisions,
        FuturesTestnetShadowSettings shadowSettings,
        string keySafetyStatus)
    {
        var evalUtc = decisions.Count > 0 ? decisions.Max(d => d.TimestampUtc) : runAtUtc;
        var wouldCount = decisions.Count(d => d.WouldPlaceOrder);
        var activationCount = decisions.Count(d => d.ActivationPassed);
        var entryCount = decisions.Count(d => d.EntrySignalPresent);
        var compact = $"futures-testnet-shadow: profiles={decisions.Count}, activationPassed={activationCount}, entrySignals={entryCount}, wouldPlace={wouldCount}, realOrdersPlaced=false, liveFuturesRecommended=false, keySafety={keySafetyStatus}";

        return new FuturesTestnetShadowSummaryRow
        {
            RunAtUtc = runAtUtc,
            EvaluationUtc = evalUtc,
            Mode = FuturesTestnetShadowCatalog.ModeName,
            BacktestOnly = true,
            TestnetShadowOnly = true,
            RealOrdersPlaced = false,
            LiveFuturesRecommended = false,
            DryRunOnly = shadowSettings.DryRunOnly,
            AllowTestnetOrders = shadowSettings.AllowTestnetOrders,
            AllowRealOrders = shadowSettings.AllowRealOrders,
            ShadowEnabledInConfig = shadowSettings.Enabled,
            KeySafetyStatus = keySafetyStatus,
            ProfilesEvaluated = decisions.Count,
            ActivationPassedCount = activationCount,
            EntrySignalCount = entryCount,
            WouldPlaceOrderCount = wouldCount,
            CompactSummaryLine = compact
        };
    }

    private static FuturesTestnetShadowRunResult BuildBlockedResult(
        DateTime runAtUtc,
        FuturesTestnetShadowSettings shadowSettings,
        FuturesTestnetShadowKeySafety.KeySafetyResult keySafety)
    {
        var summary = new FuturesTestnetShadowSummaryRow
        {
            RunAtUtc = runAtUtc,
            EvaluationUtc = runAtUtc,
            Mode = FuturesTestnetShadowCatalog.ModeName,
            BacktestOnly = true,
            TestnetShadowOnly = true,
            RealOrdersPlaced = false,
            LiveFuturesRecommended = false,
            DryRunOnly = shadowSettings.DryRunOnly,
            AllowTestnetOrders = shadowSettings.AllowTestnetOrders,
            AllowRealOrders = false,
            ShadowEnabledInConfig = shadowSettings.Enabled,
            KeySafetyStatus = keySafety.Status,
            ProfilesEvaluated = 0,
            CompactSummaryLine = $"BLOCKED: {keySafety.Status}. realOrdersPlaced=false, liveFuturesRecommended=false."
        };

        return new FuturesTestnetShadowRunResult(
            summary,
            [],
            [],
            keySafety.Status,
            SafetyBlockedRealKeys: true,
            [],
            false);
    }

    private static FuturesTestnetShadowDecisionRow BlockedDecision(
        FuturesTestnetShadowCatalog.ProfileRef profile,
        DateTime evalUtc,
        string riskStatus,
        string reason,
        FuturesTestnetShadowSettings shadowSettings,
        string dataDirectory)
    {
        var incubationOutputRoot = FuturesTestnetShadowForwardEvidenceLoader.IncubationOutputRootFromDataDirectory(dataDirectory);
        var forwardEvidence = FuturesTestnetShadowForwardEvidenceLoader.Load(
            profile, dataDirectory, incubationOutputRoot, profile.ProfileName);
        return BlockedDecisionCore(profile, evalUtc, riskStatus, reason, shadowSettings, forwardEvidence);
    }

    private static FuturesTestnetShadowDecisionRow BlockedDecisionCore(
        FuturesTestnetShadowCatalog.ProfileRef profile,
        DateTime evalUtc,
        string riskStatus,
        string reason,
        FuturesTestnetShadowSettings shadowSettings,
        FuturesTestnetShadowForwardEvidenceLoader.ForwardEvidenceSnapshot forwardEvidence)
    {
        var forwardEvidencePassed = FuturesTestnetShadowForwardEvidenceLoader.ComputeForwardEvidencePassed(
            forwardEvidence, shadowSettings.RequireForwardTradeEvidence);
        return new FuturesTestnetShadowDecisionRow
        {
            TimestampUtc = evalUtc,
            ProfileName = profile.ProfileName,
            Symbol = profile.Symbol.ToString(),
            Interval = profile.Interval,
            Direction = profile.IsBnbRule01 ? LongShortDirection.Short.ToString() : profile.ComboKey!.Direction.ToString(),
            RiskStatus = riskStatus,
            ReasonIfBlocked = reason,
            RequireForwardTradeEvidence = shadowSettings.RequireForwardTradeEvidence,
            ForwardTradeCount = forwardEvidence.ForwardTradeCount,
            ForwardNetModerate = forwardEvidence.ForwardNetModerate,
            ForwardNetStressPlus = forwardEvidence.ForwardNetStressPlus,
            ForwardEvidencePassed = forwardEvidencePassed,
            ForwardEvidenceSourceFile = forwardEvidence.ForwardEvidenceSourceFile,
            ForwardEvidenceSourceProfileName = forwardEvidence.ForwardEvidenceSourceProfileName,
            ForwardEvidenceWindowStartUtc = forwardEvidence.WindowStartUtc,
            ForwardEvidenceWindowEndUtc = forwardEvidence.WindowEndUtc,
            ForwardEvidenceIsTrueForwardOnly = forwardEvidence.ForwardEvidenceIsTrueForwardOnly
        };
    }

    private async Task WriteRunMetadataAsync(
        FuturesTestnetShadowRunResult result,
        FuturesTestnetShadowSettings shadowSettings,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = FuturesTestnetShadowCatalog.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                frozenProfiles = FuturesTestnetShadowCatalog.FrozenProfileNames,
                result.Summary.RunAtUtc,
                evaluationUtc = result.Summary.EvaluationUtc,
                result.Summary.CompactSummaryLine,
                backtestOnly = true,
                testnetShadowOnly = true,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                noNewOptimization = true,
                frozenRuleOnly = true,
                forwardOnlyJudgment = false,
                diagnosticReplayIsNotForwardProof = true,
                keySafetyStatus = result.KeySafetyStatus,
                safetyBlockedRealKeys = result.SafetyBlockedRealKeys,
                bootstrapAttempted = result.BootstrapAttempted,
                downloadOutcomes = result.DownloadOutcomes.Select(o => new { o.Symbol, o.SourceKey, o.Success, o.AddedCount, o.TotalCount, o.Message }),
                futuresTestnetShadow = new
                {
                    shadowSettings.Enabled,
                    shadowSettings.DryRunOnly,
                    shadowSettings.AllowTestnetOrders,
                    allowRealOrders = shadowSettings.AllowRealOrders,
                    maxNotionalUsdt = shadowSettings.MaxNotionalUsdt,
                    shadowSettings.Leverage,
                    shadowSettings.RequireForwardTradeEvidence
                },
                profilesEvaluated = result.Summary.ProfilesEvaluated,
                activationPassedCount = result.Summary.ActivationPassedCount,
                entrySignalCount = result.Summary.EntrySignalCount,
                wouldPlaceOrderCount = result.Summary.WouldPlaceOrderCount
            }, JsonOptions),
            cancellationToken);
    }

    private string ResolveDiscoveryJsonPath()
    {
        if (!string.IsNullOrWhiteSpace(settings.DirectionalRuleDiscoveryJsonPath))
            return settings.DirectionalRuleDiscoveryJsonPath;

        var repoRoot = Directory.GetCurrentDirectory();
        var defaultPath = Path.Combine(repoRoot, "TradingBot.Backtest", "output", "long-short-feasibility-v1", "long-short-entry-time-rule-discovery.json");
        return File.Exists(defaultPath) ? defaultPath : settings.DirectionalRuleDiscoveryJsonPath ?? defaultPath;
    }

    private async Task RefreshCandlesAsync(HistoricalKlineDataLoader loader, CancellationToken cancellationToken)
    {
        var downloader = new BinanceKlineBootstrapDownloader();
        foreach (var symbol in new[] { TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT, TradingSymbol.BTCUSDT })
        {
            var existing = await loader.LoadAndValidateAsync(symbol, cancellationToken);
            var lastUtc = existing.Candles.Count > 0 ? existing.Candles[^1].OpenTimeUtc : DateTime.UtcNow.AddDays(-35);
            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - lastUtc) < TimeSpan.FromMinutes(30))
                continue;
            var path = Path.Combine(settings.DataDirectory, $"{symbol}-1m.json");
            await downloader.DownloadAndMergeToJsonAsync(
                symbol.ToString(), path, 1000, lastUtc.AddHours(-2), nowUtc, cancellationToken);
        }
    }

}
