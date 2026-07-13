using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Decision;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.SpotFuturesCrossMarket;

public sealed class AdaptiveRollingProfitExitV1Worker(
    IServiceScopeFactory scopeFactory,
    AdaptiveRollingFuturesMarketDataService marketDataService,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<AdaptiveRollingProfitExitV1Worker> logger) : BackgroundService
{
    private const string Env = SpotFuturesCrossMarketSettings.ExecutionEnvironment;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<long, int> _exitConfirmations = new();
    private readonly ConcurrentDictionary<long, DateTime> _lastEvaluationPersistUtc = new();
    private readonly ConcurrentDictionary<long, DateTime> _lastDynamicUpdateUtc = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = AdaptiveRollingProfitExitV1Settings.Load(configuration);
        if (!settings.Enabled)
        {
            logger.LogInformation("AdaptiveRollingProfitExitV1 disabled by config. Worker not running.");
            return;
        }

        SpotFuturesCrossMarketSettings crossSettings;
        try
        {
            crossSettings = SpotFuturesCrossMarketSettings.Load(configuration, hostEnvironment.ContentRootPath);
            crossSettings.ValidateTestnetSafety(configuration);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "AdaptiveRollingProfitExitV1 cannot start because SpotFuturesCrossMarket settings are unsafe or invalid.");
            throw;
        }

        logger.LogInformation(
            "AdaptiveRollingProfitExitV1 worker started. ApplicationId={ApplicationId} AccountKey={AccountKey} Symbols={Symbols} EvaluationIntervalMs={IntervalMs} MinNetProfitUsdt={MinNetProfitUsdt} MinNetProfitBps={MinNetProfitBps} DwellMs={DwellMs} Consecutive={Consecutive} GivebackUsdt={GivebackUsdt} GivebackPercent={GivebackPercent} DynamicTpSl={DynamicTpSl}",
            settings.ApplicationId,
            settings.AccountKey,
            string.Join(",", crossSettings.Symbols),
            settings.EvaluationIntervalMs,
            settings.MinNetProfitUsdt,
            settings.MinNetProfitBps,
            settings.EligibilityDwellMs,
            settings.EligibilityConsecutiveObservations,
            settings.GivebackAbsoluteUsdt,
            settings.GivebackPercent,
            settings.EnableDynamicTakeProfitStopLoss);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(settings, crossSettings, stoppingToken);
                await Task.Delay(TimeSpan.FromMilliseconds(settings.EvaluationIntervalMs), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AdaptiveRollingProfitExitV1 cycle failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunCycleAsync(
        AdaptiveRollingProfitExitV1Settings settings,
        SpotFuturesCrossMarketSettings crossSettings,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var positionRepository = sp.GetRequiredService<IPositionRepository>();
        var rollingRepository = sp.GetRequiredService<IAdaptiveRollingProfitExitRepository>();

        var openPositions = await positionRepository.GetOpenPositionsByEnvironmentAsync(Env, cancellationToken);
        var counterfactuals = await rollingRepository.GetActiveCounterfactualsAsync(cancellationToken);
        var symbols = openPositions.Select(p => p.Symbol)
            .Concat(counterfactuals.Select(c => c.Symbol))
            .Distinct()
            .ToArray();

        foreach (var symbol in symbols)
            await marketDataService.EnsureSubscribedAsync(symbol, settings, cancellationToken);

        var counts = new Dictionary<AdaptiveRollingProfitExitState, int>();
        foreach (var position in openPositions)
        {
            var state = await EvaluatePositionAsync(position, settings, crossSettings, sp, cancellationToken);
            counts[state.State] = counts.TryGetValue(state.State, out var count) ? count + 1 : 1;
        }

        await RunCounterfactualsAsync(counterfactuals, settings, sp, cancellationToken);

        logger.LogInformation(
            "AdaptiveRollingProfitExitV1 heartbeat. OpenPositions={OpenPositions} Counterfactuals={Counterfactuals} Monitoring={Monitoring} Eligible={Eligible} Armed={Armed} Riding={Riding} Closing={Closing}",
            openPositions.Count,
            counterfactuals.Count,
            counts.GetValueOrDefault(AdaptiveRollingProfitExitState.Monitoring),
            counts.GetValueOrDefault(AdaptiveRollingProfitExitState.ProfitEligible),
            counts.GetValueOrDefault(AdaptiveRollingProfitExitState.ProfitArmed),
            counts.GetValueOrDefault(AdaptiveRollingProfitExitState.RidingTrend),
            counts.GetValueOrDefault(AdaptiveRollingProfitExitState.Closing));
    }

    private async Task<AdaptiveRollingProfitExitStateRecord> EvaluatePositionAsync(
        Position position,
        AdaptiveRollingProfitExitV1Settings settings,
        SpotFuturesCrossMarketSettings crossSettings,
        IServiceProvider sp,
        CancellationToken cancellationToken)
    {
        var rollingRepository = sp.GetRequiredService<IAdaptiveRollingProfitExitRepository>();
        var feeService = sp.GetRequiredService<AdaptiveRollingFuturesFeeService>();
        var positionRepository = sp.GetRequiredService<IPositionRepository>();
        var now = DateTime.UtcNow;

        var state = await rollingRepository.GetStateAsync(position.Id, cancellationToken)
                    ?? InitializeState(position, crossSettings, settings, now);

        if (position.IsClosing)
        {
            state.State = AdaptiveRollingProfitExitState.Closing;
            state.LastDecision = "SkippedPositionAlreadyClosing";
            state.LastRejectionReason = "Position.IsClosing is already true.";
            state.LastEvaluatedAtUtc = now;
            await rollingRepository.UpsertStateAsync(state, cancellationToken);
            return state;
        }

        var snapshot = marketDataService.GetSnapshot(position.Symbol, position.Side, position.Quantity, settings);
        if (!snapshot.IsFresh)
        {
            state.LastDecision = "SkippedMarketDataDegraded";
            state.LastRejectionReason = snapshot.DegradedReason ?? "MarketDataUnavailable";
            state.LastEvaluatedAtUtc = now;
            await rollingRepository.UpsertStateAsync(state, cancellationToken);
            await PersistEvaluationAsync(rollingRepository, state, position, snapshot, null, "SkippedMarketDataDegraded", state.LastRejectionReason, false, cancellationToken);
            logger.LogWarning(
                "AdaptiveRollingProfitExitV1 skipped rolling decision because market data is degraded. PositionId={PositionId} Symbol={Symbol} Reason={Reason} AgeMs={AgeMs} LatencyMs={LatencyMs}",
                position.Id,
                position.Symbol,
                state.LastRejectionReason,
                snapshot.MarketDataAgeMs,
                snapshot.StreamLatencyMs);
            return state;
        }

        var feeRate = await feeService.ResolveAsync(position.Symbol, settings, cancellationToken);
        var funding = await feeService.ResolveSignedFundingAsync(position, settings, state.LastFunding, cancellationToken);
        var reserveBps = Math.Max(settings.LatencyReserveBps, snapshot.RealizedVolatilityBps * settings.VolatilityReserveMultiplier);
        var adverseMoveReserve = snapshot.EstimatedCloseVwap * position.Quantity * reserveBps / 10_000m;
        var actualEntryFee = AdaptiveRollingProfitExitCalculator.ActualEntryCommissionFromOpenPosition(position.RealizedPnl);
        var projection = AdaptiveRollingProfitExitCalculator.Calculate(
            position.Side,
            position.AveragePrice,
            snapshot.EstimatedCloseVwap,
            position.Quantity,
            actualEntryFee,
            feeRate.TakerCommissionRate,
            funding,
            adverseMoveReserve);

        var trendFlow = ComputeTrendFlowScore(position.Side, snapshot, settings);
        UpdateStateFromProjection(state, position, snapshot, projection, feeRate, trendFlow, funding, now);

        var decision = Decide(state, position, snapshot, projection, trendFlow, settings, now);
        state.LastDecision = decision.Label;
        state.LastRejectionReason = decision.RejectionReason;

        if (settings.EnableDynamicTakeProfitStopLoss &&
            decision.ShouldClose is false &&
            state.State is AdaptiveRollingProfitExitState.ProfitArmed or AdaptiveRollingProfitExitState.RidingTrend)
        {
            await TryUpdateDynamicProtectionAsync(position, state, snapshot, projection, trendFlow, settings, positionRepository, rollingRepository, cancellationToken);
        }

        var shouldPersistEvaluation = ShouldPersistEvaluation(position.Id, settings, now)
                                      || decision.StateTransitioned
                                      || decision.ShouldClose
                                      || decision.Label.Contains("Peak", StringComparison.OrdinalIgnoreCase);
        await rollingRepository.UpsertStateAsync(state, cancellationToken);

        if (shouldPersistEvaluation)
            await PersistEvaluationAsync(rollingRepository, state, position, snapshot, projection, decision.Label, decision.RejectionReason, true, cancellationToken);

        logger.LogInformation(
            "AdaptiveRollingProfitExitV1 evaluated. PositionId={PositionId} Symbol={Symbol} Side={Side} State={State} NetPnl={NetPnl:F6} Peak={Peak:F6} Giveback={Giveback:F6} Score={Score:F2} Decision={Decision} Reason={Reason}",
            position.Id,
            position.Symbol,
            position.Side,
            state.State,
            projection.ProjectedNetPnl,
            state.PeakProjectedNetPnl,
            Math.Max(0m, state.PeakProjectedNetPnl - projection.ProjectedNetPnl),
            trendFlow.Score,
            decision.Label,
            decision.RejectionReason);

        if (decision.ShouldClose)
            await ExecuteRollingCloseAsync(position, state, settings, sp, decision, cancellationToken);

        return state;
    }

    private static AdaptiveRollingProfitExitStateRecord InitializeState(
        Position position,
        SpotFuturesCrossMarketSettings crossSettings,
        AdaptiveRollingProfitExitV1Settings settings,
        DateTime now)
    {
        var maxHoldMinutes = crossSettings.MaxHoldMinutes > 0
            ? crossSettings.MaxHoldMinutes
            : settings.OriginalMaxHoldMinutesFallback;

        return new AdaptiveRollingProfitExitStateRecord
        {
            PositionId = position.Id,
            Symbol = position.Symbol,
            Side = position.Side,
            State = AdaptiveRollingProfitExitState.Monitoring,
            RemainingQuantity = position.Quantity,
            EntryPrice = position.AveragePrice,
            EntryNotional = position.AveragePrice * position.Quantity,
            OriginalStopLossPrice = position.StopLossPrice,
            OriginalTakeProfitPrice = position.TakeProfitPrice,
            CurrentStopLossPrice = position.StopLossPrice,
            CurrentTakeProfitPrice = position.TakeProfitPrice,
            OriginalMaxHoldUntilUtc = (position.OpenedAt ?? now).AddMinutes(maxHoldMinutes),
            LastTransitionAtUtc = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static void UpdateStateFromProjection(
        AdaptiveRollingProfitExitStateRecord state,
        Position position,
        AdaptiveRollingMarketDataSnapshot snapshot,
        AdaptiveRollingProfitProjectedPnl projection,
        FuturesCommissionRate feeRate,
        AdaptiveRollingTrendFlow trendFlow,
        decimal funding,
        DateTime now)
    {
        state.RemainingQuantity = position.Quantity;
        state.EntryPrice = position.AveragePrice;
        state.EntryNotional = position.AveragePrice * position.Quantity;
        state.LastProjectedNetPnl = projection.ProjectedNetPnl;
        state.LastGrossProjectedPnl = projection.GrossPnl;
        state.LastEstimatedExitFee = projection.EstimatedExitCommission;
        state.LastActualEntryFee = projection.ActualEntryCommissions;
        state.LastFunding = funding;
        state.LastAdverseMoveReserve = projection.AdverseMoveReserve;
        state.LastSpreadBps = snapshot.SpreadBps;
        state.LastEstimatedSlippageBps = snapshot.EstimatedSlippageBps;
        state.LastTrendFlowScore = trendFlow.Score;
        state.LastFeeSource = feeRate.Source;
        state.LastFeeAgeSeconds = (long)Math.Max(0, (now - feeRate.FetchedAtUtc).TotalSeconds);
        state.LastEvaluatedAtUtc = now;
    }

    private RollingDecision Decide(
        AdaptiveRollingProfitExitStateRecord state,
        Position position,
        AdaptiveRollingMarketDataSnapshot snapshot,
        AdaptiveRollingProfitProjectedPnl projection,
        AdaptiveRollingTrendFlow trendFlow,
        AdaptiveRollingProfitExitV1Settings settings,
        DateTime now)
    {
        var entryNotional = Math.Max(state.EntryNotional, position.AveragePrice * position.Quantity);
        var armThreshold = settings.EntryProfitArmThreshold(entryNotional);
        var closeFloor = settings.CloseProfitFloor(entryNotional);
        var profitableEnoughToArm = projection.ProjectedNetPnl >= armThreshold;
        var previousState = state.State;

        if (profitableEnoughToArm)
        {
            state.EligibleSinceUtc ??= now;
            state.ConsecutiveProfitableObservations++;
        }
        else
        {
            state.EligibleSinceUtc = null;
            state.ConsecutiveProfitableObservations = 0;
            if (state.State is AdaptiveRollingProfitExitState.Monitoring or AdaptiveRollingProfitExitState.ProfitEligible)
            {
                Transition(state, AdaptiveRollingProfitExitState.Monitoring, now);
            }

            _exitConfirmations.TryRemove(position.Id, out _);
            return new RollingDecision("HoldBelowArmThreshold", $"Projected net {projection.ProjectedNetPnl:F6} < arm threshold {armThreshold:F6}.", false, previousState != state.State);
        }

        var dwellMet = state.EligibleSinceUtc.HasValue &&
                       (now - state.EligibleSinceUtc.Value).TotalMilliseconds >= settings.EligibilityDwellMs;
        var consecutiveMet = state.ConsecutiveProfitableObservations >= settings.EligibilityConsecutiveObservations;

        if (state.State is AdaptiveRollingProfitExitState.Monitoring or AdaptiveRollingProfitExitState.DisabledOrDegraded)
        {
            if (!dwellMet && !consecutiveMet)
                return new RollingDecision("EligibilityDwellPending", $"Net profit threshold held {state.ConsecutiveProfitableObservations}/{settings.EligibilityConsecutiveObservations} observations.", false, previousState != state.State);

            Transition(state, AdaptiveRollingProfitExitState.ProfitEligible, now);
            return new RollingDecision("ProfitEligible", $"Projected net {projection.ProjectedNetPnl:F6} >= arm threshold {armThreshold:F6}.", false, true);
        }

        if (state.State == AdaptiveRollingProfitExitState.ProfitEligible)
        {
            Transition(state, AdaptiveRollingProfitExitState.ProfitArmed, now);
            state.ArmedAtUtc = now;
            state.ArmingExecutablePrice = projection.EstimatedExecutablePrice;
            state.ArmingProjectedNetPnl = projection.ProjectedNetPnl;
            state.ArmingFeeSnapshotJson = JsonSerializer.Serialize(new
            {
                state.LastFeeSource,
                state.LastFeeAgeSeconds,
                state.LastActualEntryFee,
                state.LastEstimatedExitFee,
                state.LastFunding,
                state.LastAdverseMoveReserve
            }, JsonOptions);
            state.ArmingTrendFlowSnapshotJson = JsonSerializer.Serialize(trendFlow, JsonOptions);
            UpdatePeak(state, projection, now);
            return new RollingDecision("ProfitArmed", "Profit protection armed after sustained net-profit threshold.", false, true);
        }

        if (state.State is not (AdaptiveRollingProfitExitState.ProfitArmed or AdaptiveRollingProfitExitState.RidingTrend))
            return new RollingDecision("HoldStateNotArmed", $"State {state.State} is not eligible for rolling exit.", false, previousState != state.State);

        var peakChanged = UpdatePeak(state, projection, now);
        if (state.State == AdaptiveRollingProfitExitState.ProfitArmed && trendFlow.Score >= settings.RideTrendScoreMin)
            Transition(state, AdaptiveRollingProfitExitState.RidingTrend, now);

        if (projection.ProjectedNetPnl < closeFloor)
        {
            _exitConfirmations.TryRemove(position.Id, out _);
            return new RollingDecision(
                peakChanged ? "PeakUpdatedHoldBelowCloseFloor" : "HoldBelowCloseFloor",
                $"Projected net {projection.ProjectedNetPnl:F6} < close floor {closeFloor:F6}.",
                false,
                previousState != state.State || peakChanged);
        }

        if (state.LastTransitionAtUtc.HasValue &&
            (now - state.LastTransitionAtUtc.Value).TotalMilliseconds < settings.StateHysteresisMs)
        {
            _exitConfirmations.TryRemove(position.Id, out _);
            return new RollingDecision("HoldHysteresis", "State hysteresis window is still active.", false, previousState != state.State || peakChanged);
        }

        var giveback = Math.Max(0m, state.PeakProjectedNetPnl - projection.ProjectedNetPnl);
        var givebackPercent = state.PeakProjectedNetPnl > 0m ? giveback / state.PeakProjectedNetPnl * 100m : 0m;
        var givebackTriggered = IsThresholdTriggered(giveback, givebackPercent, settings.GivebackAbsoluteUsdt, settings.GivebackPercent);
        var weakTrend = trendFlow.Score <= settings.WeakTrendScoreMax ||
                        (trendFlow.DirectionNormalizedVelocityBps < 0m && trendFlow.DirectionNormalizedFlowImbalance < 0m);
        var strongReversal = trendFlow.Score <= settings.StrongReversalScoreMax &&
                             trendFlow.DirectionNormalizedVelocityBps < 0m &&
                             trendFlow.DirectionNormalizedFlowImbalance < 0m;
        var hardTierReached = settings.EnableHardProfitLock &&
                              state.PeakProjectedNetPnl >= settings.HardProfitTier(entryNotional);
        var hardGiveback = IsThresholdTriggered(giveback, givebackPercent, settings.HardProfitLockGivebackUsdt, settings.HardProfitLockGivebackPercent);

        string? exitLabel = null;
        string? exitReason = null;
        if (hardTierReached && hardGiveback)
        {
            exitLabel = "ExitHardProfitLock";
            exitReason = $"Hard profit lock: peak={state.PeakProjectedNetPnl:F6}, giveback={giveback:F6} ({givebackPercent:F2}%).";
        }
        else if (strongReversal)
        {
            exitLabel = "ExitStrongReversal";
            exitReason = $"Strong reversal: score={trendFlow.Score:F2}, velocity={trendFlow.DirectionNormalizedVelocityBps:F2}bps, flow={trendFlow.DirectionNormalizedFlowImbalance:F4}.";
        }
        else if (givebackTriggered && weakTrend)
        {
            exitLabel = "ExitConfirmedGiveback";
            exitReason = $"Confirmed giveback: peak={state.PeakProjectedNetPnl:F6}, current={projection.ProjectedNetPnl:F6}, score={trendFlow.Score:F2}.";
        }

        if (exitLabel is null)
        {
            _exitConfirmations.TryRemove(position.Id, out _);
            var holdLabel = trendFlow.Score >= settings.RideTrendScoreMin ? "RideTrend" : "HoldArmed";
            return new RollingDecision(peakChanged ? $"{holdLabel}PeakUpdated" : holdLabel, "No rolling exit condition met.", false, previousState != state.State || peakChanged);
        }

        var confirmations = _exitConfirmations.AddOrUpdate(position.Id, 1, (_, count) => count + 1);
        if (confirmations < settings.ExitConfirmationObservations && exitLabel != "ExitHardProfitLock")
        {
            return new RollingDecision("ExitConfirmationPending", $"{exitReason} Confirmation {confirmations}/{settings.ExitConfirmationObservations}.", false, previousState != state.State || peakChanged);
        }

        Transition(state, AdaptiveRollingProfitExitState.ExitPending, now);
        return new RollingDecision(exitLabel, exitReason, true, true);
    }

    private async Task ExecuteRollingCloseAsync(
        Position position,
        AdaptiveRollingProfitExitStateRecord state,
        AdaptiveRollingProfitExitV1Settings settings,
        IServiceProvider sp,
        RollingDecision decision,
        CancellationToken cancellationToken)
    {
        var closeService = sp.GetRequiredService<SpotFuturesCrossMarketCloseOrderService>();
        var rollingRepository = sp.GetRequiredService<IAdaptiveRollingProfitExitRepository>();
        var decisionRepository = sp.GetRequiredService<ITradeExecutionDesicionsRepository>();
        var correlationId = Guid.NewGuid().ToString("N");
        var acceptedAt = DateTime.UtcNow;
        state.CloseCorrelationId = correlationId;
        state.CloseSubmittedAtUtc = acceptedAt;
        await rollingRepository.UpsertStateAsync(state, cancellationToken);

        var closeResult = await closeService.CloseAsync(
            new SpotFuturesCrossMarketCloseRequest(
                SpotFuturesCrossMarketSettings.Load(configuration, hostEnvironment.ContentRootPath).ForSymbol(position.Symbol),
                position,
                PositionExitReason.RollingProfit,
                CloseReason.RollingProfit,
                $"{decision.Label}: {decision.RejectionReason}",
                correlationId,
                OrderSource.AdaptiveRollingProfitExitV1,
                acceptedAt,
                position.Quantity,
                settings.CloseLockSeconds),
            cancellationToken);

        if (closeResult.Success && closeResult.Order is not null && closeResult.ClosedPosition is not null)
        {
            state.State = AdaptiveRollingProfitExitState.Closed;
            state.RemainingQuantity = 0m;
            state.CloseLocalOrderId = closeResult.Order.Id;
            state.CloseExchangeOrderId = closeResult.Order.ExchangeOrderId;
            state.CloseSubmittedAtUtc = closeResult.SubmittedAtUtc;
            state.CloseAcknowledgedAtUtc = closeResult.AcknowledgedAtUtc;
            state.CloseFilledAtUtc = closeResult.FilledAtUtc;
            state.ActualRealizedGrossPnl = closeResult.ActualGrossPnl;
            state.ActualRealizedNetPnl = closeResult.ActualNetPnl;
            state.LastDecision = decision.Label;
            state.LastRejectionReason = decision.RejectionReason;
            state.LastTransitionAtUtc = DateTime.UtcNow;
            await rollingRepository.UpsertStateAsync(state, cancellationToken);
            await PersistRollingDecisionAsync(decisionRepository, position, decision, DecisionStatus.Executed, closeResult.Order, null, cancellationToken);
            await StartCounterfactualAsync(rollingRepository, state, closeResult.ClosedPosition, closeResult.Order.Price, cancellationToken);
            logger.LogInformation(
                "AdaptiveRollingProfitExitV1 rolling close completed. PositionId={PositionId} LocalOrderId={LocalOrderId} ExchangeOrderId={ExchangeOrderId} ActualNetPnl={ActualNetPnl:F6}",
                position.Id,
                closeResult.Order.Id,
                closeResult.Order.ExchangeOrderId,
                closeResult.ActualNetPnl);
            return;
        }

        state.State = closeResult.DuplicatePrevented ? AdaptiveRollingProfitExitState.Closing : AdaptiveRollingProfitExitState.ProfitArmed;
        state.LastDecision = closeResult.DuplicatePrevented ? "DuplicateClosePrevented" : "RollingCloseFailed";
        state.LastRejectionReason = closeResult.Error;
        state.LastTransitionAtUtc = DateTime.UtcNow;
        await rollingRepository.UpsertStateAsync(state, cancellationToken);
        await PersistRollingDecisionAsync(decisionRepository, position, decision, DecisionStatus.Failed, closeResult.Order, closeResult.Error, cancellationToken);
    }

    private async Task TryUpdateDynamicProtectionAsync(
        Position position,
        AdaptiveRollingProfitExitStateRecord state,
        AdaptiveRollingMarketDataSnapshot snapshot,
        AdaptiveRollingProfitProjectedPnl projection,
        AdaptiveRollingTrendFlow trendFlow,
        AdaptiveRollingProfitExitV1Settings settings,
        IPositionRepository positionRepository,
        IAdaptiveRollingProfitExitRepository rollingRepository,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_lastDynamicUpdateUtc.TryGetValue(position.Id, out var last) &&
            now - last < TimeSpan.FromSeconds(settings.DynamicOrderUpdateCooldownSeconds))
        {
            return;
        }

        var changed = false;
        var isLong = position.Side == OrderSide.BUY;
        var currentStop = position.StopLossPrice;
        var lockProfit = state.EntryNotional * settings.StopLossLockProfitBps / 10_000m;
        var lockedBreakEven = isLong
            ? projection.BreakEvenExecutablePrice + lockProfit / Math.Max(position.Quantity, 0.00000001m)
            : projection.BreakEvenExecutablePrice - lockProfit / Math.Max(position.Quantity, 0.00000001m);

        if (isLong)
        {
            if (lockedBreakEven > 0m &&
                (!currentStop.HasValue || lockedBreakEven > currentStop.Value) &&
                lockedBreakEven < snapshot.BestBidPrice)
            {
                position.StopLossPrice = lockedBreakEven;
                state.CurrentStopLossPrice = lockedBreakEven;
                changed = true;
            }

            if (trendFlow.Score >= settings.RideTrendScoreMin && state.OriginalTakeProfitPrice.HasValue && position.TakeProfitPrice.HasValue)
            {
                var cap = state.OriginalTakeProfitPrice.Value + state.EntryPrice * settings.TakeProfitExtensionMaxBps / 10_000m;
                var candidate = Math.Min(cap, position.TakeProfitPrice.Value + state.EntryPrice * settings.TakeProfitExtensionStepBps / 10_000m);
                if (candidate > position.TakeProfitPrice.Value)
                {
                    position.TakeProfitPrice = candidate;
                    state.CurrentTakeProfitPrice = candidate;
                    changed = true;
                }
            }
        }
        else
        {
            if (lockedBreakEven > 0m &&
                (!currentStop.HasValue || lockedBreakEven < currentStop.Value) &&
                lockedBreakEven > snapshot.BestAskPrice)
            {
                position.StopLossPrice = lockedBreakEven;
                state.CurrentStopLossPrice = lockedBreakEven;
                changed = true;
            }

            if (trendFlow.Score >= settings.RideTrendScoreMin && state.OriginalTakeProfitPrice.HasValue && position.TakeProfitPrice.HasValue)
            {
                var cap = state.OriginalTakeProfitPrice.Value - state.EntryPrice * settings.TakeProfitExtensionMaxBps / 10_000m;
                var candidate = Math.Max(cap, position.TakeProfitPrice.Value - state.EntryPrice * settings.TakeProfitExtensionStepBps / 10_000m);
                if (candidate < position.TakeProfitPrice.Value)
                {
                    position.TakeProfitPrice = candidate;
                    state.CurrentTakeProfitPrice = candidate;
                    changed = true;
                }
            }
        }

        if (!changed)
            return;

        await positionRepository.UpsertAsync(position, cancellationToken);
        state.LastDecision = "DynamicProtectionUpdated";
        state.LastRejectionReason = "Application-level SL/TP thresholds updated atomically in positions table.";
        await rollingRepository.UpsertStateAsync(state, cancellationToken);
        _lastDynamicUpdateUtc[position.Id] = now;
        logger.LogInformation(
            "AdaptiveRollingProfitExitV1 dynamic protection updated. PositionId={PositionId} StopLoss={StopLoss} TakeProfit={TakeProfit}",
            position.Id,
            position.StopLossPrice,
            position.TakeProfitPrice);
    }

    private async Task RunCounterfactualsAsync(
        IReadOnlyList<AdaptiveRollingProfitCounterfactualRecord> counterfactuals,
        AdaptiveRollingProfitExitV1Settings settings,
        IServiceProvider sp,
        CancellationToken cancellationToken)
    {
        if (counterfactuals.Count == 0)
            return;

        var rollingRepository = sp.GetRequiredService<IAdaptiveRollingProfitExitRepository>();
        var feeService = sp.GetRequiredService<AdaptiveRollingFuturesFeeService>();
        foreach (var counterfactual in counterfactuals)
        {
            var snapshot = marketDataService.GetSnapshot(counterfactual.Symbol, counterfactual.Side, counterfactual.Quantity, settings);
            if (!snapshot.IsFresh || snapshot.MarkPrice <= 0m)
                continue;

            var mark = snapshot.MarkPrice;
            if (counterfactual.Side == OrderSide.BUY)
            {
                counterfactual.MaxAdditionalFavorableMove = Math.Max(counterfactual.MaxAdditionalFavorableMove, Math.Max(0m, mark - counterfactual.ActualRollingExitPrice));
                counterfactual.MaxAvoidedAdverseMove = Math.Max(counterfactual.MaxAvoidedAdverseMove, Math.Max(0m, counterfactual.ActualRollingExitPrice - mark));
            }
            else
            {
                counterfactual.MaxAdditionalFavorableMove = Math.Max(counterfactual.MaxAdditionalFavorableMove, Math.Max(0m, counterfactual.ActualRollingExitPrice - mark));
                counterfactual.MaxAvoidedAdverseMove = Math.Max(counterfactual.MaxAvoidedAdverseMove, Math.Max(0m, mark - counterfactual.ActualRollingExitPrice));
            }

            string? exitReason = null;
            var isLong = counterfactual.Side == OrderSide.BUY;
            if (counterfactual.OriginalTakeProfitPrice.HasValue &&
                (isLong ? mark >= counterfactual.OriginalTakeProfitPrice.Value : mark <= counterfactual.OriginalTakeProfitPrice.Value))
            {
                exitReason = "OriginalTakeProfit";
                mark = counterfactual.OriginalTakeProfitPrice.Value;
            }
            else if (counterfactual.OriginalStopLossPrice.HasValue &&
                     (isLong ? mark <= counterfactual.OriginalStopLossPrice.Value : mark >= counterfactual.OriginalStopLossPrice.Value))
            {
                exitReason = "OriginalStopLoss";
                mark = counterfactual.OriginalStopLossPrice.Value;
            }
            else if (counterfactual.OriginalMaxHoldUntilUtc.HasValue && DateTime.UtcNow >= counterfactual.OriginalMaxHoldUntilUtc.Value)
            {
                exitReason = "OriginalMaxHold";
            }

            if (exitReason is not null)
            {
                var state = await rollingRepository.GetStateAsync(counterfactual.PositionId, cancellationToken);
                var feeRate = await feeService.ResolveAsync(counterfactual.Symbol, settings, cancellationToken);
                var projection = AdaptiveRollingProfitExitCalculator.Calculate(
                    counterfactual.Side,
                    counterfactual.EntryPrice,
                    mark,
                    counterfactual.Quantity,
                    state?.LastActualEntryFee ?? 0m,
                    feeRate.TakerCommissionRate,
                    state?.LastFunding ?? 0m,
                    0m);

                counterfactual.CounterfactualExitPrice = mark;
                counterfactual.CounterfactualNetPnl = projection.ProjectedNetPnl;
                counterfactual.CounterfactualExitReason = exitReason;
                counterfactual.BetterExitMethod = projection.ProjectedNetPnl > counterfactual.ActualRollingNetPnl
                    ? "OriginalFixedExit"
                    : "RollingProfitExit";
                counterfactual.IsActive = false;
                counterfactual.CompletedAtUtc = DateTime.UtcNow;
            }

            await rollingRepository.UpsertCounterfactualAsync(counterfactual, cancellationToken);
        }
    }

    private async Task StartCounterfactualAsync(
        IAdaptiveRollingProfitExitRepository rollingRepository,
        AdaptiveRollingProfitExitStateRecord state,
        Position closedPosition,
        decimal actualExitPrice,
        CancellationToken cancellationToken)
    {
        await rollingRepository.UpsertCounterfactualAsync(new AdaptiveRollingProfitCounterfactualRecord
        {
            PositionId = state.PositionId,
            Symbol = state.Symbol,
            Side = state.Side,
            EntryPrice = state.EntryPrice,
            Quantity = state.EntryNotional > 0m && state.EntryPrice > 0m ? state.EntryNotional / state.EntryPrice : closedPosition.Quantity,
            OriginalStopLossPrice = state.OriginalStopLossPrice,
            OriginalTakeProfitPrice = state.OriginalTakeProfitPrice,
            OriginalMaxHoldUntilUtc = state.OriginalMaxHoldUntilUtc,
            ActualRollingExitPrice = actualExitPrice,
            ActualRollingNetPnl = state.ActualRealizedNetPnl ?? closedPosition.RealizedPnl,
            ActualRollingClosedAtUtc = closedPosition.ClosedAt ?? DateTime.UtcNow,
            IsActive = true
        }, cancellationToken);
    }

    private async Task PersistEvaluationAsync(
        IAdaptiveRollingProfitExitRepository rollingRepository,
        AdaptiveRollingProfitExitStateRecord state,
        Position position,
        AdaptiveRollingMarketDataSnapshot snapshot,
        AdaptiveRollingProfitProjectedPnl? projection,
        string decision,
        string? rejectionReason,
        bool isMarketDataFresh,
        CancellationToken cancellationToken)
    {
        var giveback = projection is null ? 0m : Math.Max(0m, state.PeakProjectedNetPnl - projection.ProjectedNetPnl);
        var givebackPercent = state.PeakProjectedNetPnl > 0m ? giveback / state.PeakProjectedNetPnl * 100m : 0m;
        await rollingRepository.InsertEvaluationAsync(new AdaptiveRollingProfitExitEvaluationRecord
        {
            PositionId = position.Id,
            Symbol = position.Symbol,
            Side = position.Side,
            State = state.State,
            RemainingQuantity = position.Quantity,
            EstimatedExecutablePrice = projection?.EstimatedExecutablePrice ?? snapshot.EstimatedCloseVwap,
            GrossProjectedPnl = projection?.GrossPnl ?? 0m,
            ActualEntryCommissions = projection?.ActualEntryCommissions ?? 0m,
            EstimatedExitCommission = projection?.EstimatedExitCommission ?? 0m,
            Funding = projection?.Funding ?? state.LastFunding,
            AdverseMoveReserve = projection?.AdverseMoveReserve ?? 0m,
            ProjectedNetPnl = projection?.ProjectedNetPnl ?? 0m,
            BreakEvenExecutablePrice = projection?.BreakEvenExecutablePrice ?? 0m,
            PeakProjectedNetPnl = state.PeakProjectedNetPnl,
            GivebackAmount = giveback,
            GivebackPercent = givebackPercent,
            SpreadBps = snapshot.SpreadBps,
            EstimatedSlippageBps = snapshot.EstimatedSlippageBps,
            TopBidNotional = snapshot.TopBidNotional,
            TopAskNotional = snapshot.TopAskNotional,
            OrderBookImbalance = snapshot.OrderBookImbalance,
            Microprice = snapshot.Microprice,
            AggressiveBuyQuantity = snapshot.AggressiveBuyQuantity,
            AggressiveSellQuantity = snapshot.AggressiveSellQuantity,
            AggressiveFlowImbalance = snapshot.AggressiveFlowImbalance,
            NormalizedVelocityBps = position.Side == OrderSide.BUY ? snapshot.VelocityBps : -snapshot.VelocityBps,
            RealizedVolatilityBps = snapshot.RealizedVolatilityBps,
            TrendFlowScore = state.LastTrendFlowScore,
            MarketDataEventTimeUtc = snapshot.MarketDataEventTimeUtc,
            MarketDataTransactionTimeUtc = snapshot.MarketDataTransactionTimeUtc,
            MarketDataLocalReceiptUtc = snapshot.MarketDataLocalReceiptUtc,
            EvaluatedAtUtc = DateTime.UtcNow,
            MarketDataAgeMs = snapshot.MarketDataAgeMs == long.MaxValue ? int.MaxValue : snapshot.MarketDataAgeMs,
            StreamLatencyMs = snapshot.StreamLatencyMs,
            IsMarketDataFresh = isMarketDataFresh,
            Decision = decision,
            RejectionReason = rejectionReason,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions)
        }, cancellationToken);
    }

    private async Task PersistRollingDecisionAsync(
        ITradeExecutionDesicionsRepository decisionRepository,
        Position position,
        RollingDecision rollingDecision,
        DecisionStatus status,
        Order? order,
        string? error,
        CancellationToken cancellationToken)
    {
        var intent = position.Side == OrderSide.BUY ? TradeExecutionIntent.CloseLong : TradeExecutionIntent.CloseShort;
        await decisionRepository.AddDesicionAsync(new TradeExecutionDecisions
        {
            CorrelationId = Guid.NewGuid().ToString("N"),
            DecisionId = Guid.NewGuid().ToString("N"),
            StrategyName = AdaptiveRollingProfitExitV1Settings.FeatureName,
            Symbol = position.Symbol,
            Action = intent == TradeExecutionIntent.CloseLong ? TradeSignal.Sell : TradeSignal.Buy,
            RawSignal = intent == TradeExecutionIntent.CloseLong ? TradeSignal.Sell : TradeSignal.Buy,
            TradingMode = TradingMode.Futures,
            ExecutionIntent = intent,
            Side = intent == TradeExecutionIntent.CloseLong ? OrderSide.SELL : OrderSide.BUY,
            DecisionStatus = status,
            GuardStage = GuardStage.Execution,
            Reason = Truncate($"{rollingDecision.Label}: {rollingDecision.RejectionReason}", 4000),
            RiskIsAllowed = true,
            ExecutionSuccess = status == DecisionStatus.Executed,
            LocalOrderId = order?.Id,
            ExchangeOrderId = order?.ExchangeOrderId,
            ExecutionError = error is null ? null : Truncate(error, 4000)
        });
    }

    private bool ShouldPersistEvaluation(long positionId, AdaptiveRollingProfitExitV1Settings settings, DateTime now)
    {
        if (!_lastEvaluationPersistUtc.TryGetValue(positionId, out var last) ||
            (now - last).TotalMilliseconds >= settings.RoutineEvaluationSampleMs)
        {
            _lastEvaluationPersistUtc[positionId] = now;
            return true;
        }

        return false;
    }

    private static bool UpdatePeak(
        AdaptiveRollingProfitExitStateRecord state,
        AdaptiveRollingProfitProjectedPnl projection,
        DateTime now)
    {
        if (projection.ProjectedNetPnl <= state.PeakProjectedNetPnl)
            return false;

        state.PeakProjectedNetPnl = projection.ProjectedNetPnl;
        state.BestExecutablePrice = projection.EstimatedExecutablePrice;
        state.PeakUpdatedAtUtc = now;
        state.LastPeakPersistedAtUtc = now;
        return true;
    }

    private static void Transition(
        AdaptiveRollingProfitExitStateRecord state,
        AdaptiveRollingProfitExitState next,
        DateTime now)
    {
        if (state.State == next)
            return;
        state.State = next;
        state.LastTransitionAtUtc = now;
    }

    private static bool IsThresholdTriggered(decimal absoluteValue, decimal percentValue, decimal absoluteThreshold, decimal percentThreshold)
    {
        var absoluteTriggered = absoluteThreshold > 0m && absoluteValue >= absoluteThreshold;
        var percentTriggered = percentThreshold > 0m && percentValue >= percentThreshold;
        return absoluteTriggered || percentTriggered;
    }

    private static AdaptiveRollingTrendFlow ComputeTrendFlowScore(
        OrderSide side,
        AdaptiveRollingMarketDataSnapshot snapshot,
        AdaptiveRollingProfitExitV1Settings settings)
    {
        var direction = side == OrderSide.BUY ? 1m : -1m;
        var normalizedVelocity = snapshot.VelocityBps * direction;
        var normalizedFlow = snapshot.AggressiveFlowImbalance * direction;
        var normalizedBook = snapshot.OrderBookImbalance * direction;
        var normalizedMicroprice = snapshot.MicropricePressureBps * direction;

        var velocityComponent = Math.Clamp(normalizedVelocity / settings.VelocityReferenceBps, -1m, 1m) * settings.VelocityWeight;
        var flowComponent = Math.Clamp(normalizedFlow, -1m, 1m) * settings.AggressiveFlowWeight;
        var bookComponent = Math.Clamp(normalizedBook, -1m, 1m) * settings.BookImbalanceWeight;
        var micropriceComponent = Math.Clamp(normalizedMicroprice / settings.VelocityReferenceBps, -1m, 1m) * settings.MicropriceWeight;
        var score = velocityComponent + flowComponent + bookComponent + micropriceComponent;

        return new AdaptiveRollingTrendFlow(
            Math.Clamp(score, -100m, 100m),
            normalizedVelocity,
            normalizedFlow,
            normalizedBook,
            normalizedMicroprice);
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private sealed record RollingDecision(string Label, string? RejectionReason, bool ShouldClose, bool StateTransitioned);
}

public sealed record AdaptiveRollingTrendFlow(
    decimal Score,
    decimal DirectionNormalizedVelocityBps,
    decimal DirectionNormalizedFlowImbalance,
    decimal DirectionNormalizedBookImbalance,
    decimal DirectionNormalizedMicropriceBps);
