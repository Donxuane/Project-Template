using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Runs the decision pipeline on a fixed interval.
/// V1 only generates/logs decisions (execution is intentionally separate).
/// </summary>
public class DecisionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DecisionWorker> logger) : BackgroundService
{
    private const int DefaultIntervalSeconds = 30;
    private const decimal DefaultQuantity = 0.001m;
    private const string DefaultSymbol = nameof(TradingSymbol.BTCUSDT);
    private const decimal DefaultMinConfidence = 0.55m;
    private const int DefaultTradeCooldownSeconds = 60;
    private const int DefaultIdempotencyWindowSeconds = 120;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = ReadSettings();

        logger.LogInformation(
            "DecisionWorker started. Symbols={Symbols}, Quantity={Quantity}, Interval={IntervalSeconds}s, MinConfidence={MinConfidence}, TradeCooldownSeconds={TradeCooldownSeconds}, IdempotencyWindowSeconds={IdempotencyWindowSeconds}, ExecutionEnabled={ExecutionEnabled}, UseMarketOrders={UseMarketOrders}",
            string.Join(",", settings.Symbols), settings.Quantity, settings.IntervalSeconds, settings.MinConfidence, settings.TradeCooldownSeconds, settings.IdempotencyWindowSeconds, settings.ExecutionEnabled, settings.UseMarketOrders);

        using (var warmupScope = scopeFactory.CreateScope())
        {
            var candleWarmupService = warmupScope.ServiceProvider.GetRequiredService<ICandleWarmupService>();
            await candleWarmupService.WarmUpAsync(settings.Symbols, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var correlationId = Guid.NewGuid().ToString("N");
                using (var scope = scopeFactory.CreateScope())
                {
                    var tradeDecisionService = scope.ServiceProvider.GetRequiredService<TradeDesicionService>();
                    var riskManagementService = scope.ServiceProvider.GetRequiredService<IRiskManagementService>();
                    var tradeExecutionService = scope.ServiceProvider.GetRequiredService<ITradeExecutionService>();
                    var tradeCooldownService = scope.ServiceProvider.GetRequiredService<ITradeCooldownService>();
                    var tradeIdempotencyService = scope.ServiceProvider.GetRequiredService<ITradeIdempotencyService>();
                    var tradeExecutionDesicionsRepository = scope.ServiceProvider.GetRequiredService<ITradeExecutionDesicionsRepository>();
                    foreach (var symbol in settings.Symbols)
                    {
                        await ProcessSymbolAsync(
                            symbol,
                            settings,
                            correlationId,
                            stoppingToken,
                            tradeDecisionService,
                            riskManagementService,
                            tradeExecutionService,
                            tradeCooldownService,
                            tradeIdempotencyService,
                            tradeExecutionDesicionsRepository);
                    }
                }

            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DecisionWorker cycle failed at {Time}", DateTime.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), stoppingToken);
        }

        logger.LogInformation("DecisionWorker stopped.");
    }

    private async Task ProcessSymbolAsync(
        TradingSymbol symbol,
        DecisionWorkerSettings settings,
        string correlationId,
        CancellationToken stoppingToken,
        TradeDesicionService tradeDecisionService,
        IRiskManagementService riskManagementService,
        ITradeExecutionService tradeExecutionService,
        ITradeCooldownService tradeCooldownService,
        ITradeIdempotencyService tradeIdempotencyService,
        ITradeExecutionDesicionsRepository tradeExecutionDesicionsRepository
        )
    {
        try
        {
            var decision = await tradeDecisionService.MakeDesicion(symbol, settings.Quantity, stoppingToken);
            var decisionId = CreateDecisionId(symbol, settings.Quantity, decision, settings.IdempotencyWindowSeconds);

            logger.LogInformation(
                "DecisionWorker decision generated: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Action={Action}, Confidence={Confidence:F4}, Reason={Reason}",
                correlationId, decisionId, symbol, decision.Action, decision.Confidence, decision.Reason);
            var executionDesicion = new TradeExecutionDecisions
            {
                CorrelationId = correlationId,
                DecisionId = decisionId,
                Symbol = symbol,
                Action = decision.Action,
                Confidence = decision.Confidence,
                Reason = decision.Reason,
                MinConfidence = settings.MinConfidence
            };

            var executionId = await tradeExecutionDesicionsRepository.AddDesicionAsync(executionDesicion);
            if (decision.Action == TradeSignal.Hold)
                return;

            if (decision.Confidence < settings.MinConfidence)
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Reason=LowConfidence, Confidence={Confidence:F4}, Threshold={Threshold:F4}",
                    correlationId, decisionId, symbol, decision.Confidence, settings.MinConfidence);

                return;
            }

            if (!settings.ExecutionEnabled)
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Reason=SafeModeDisabled",
                    correlationId, decisionId, symbol);
                return;
            }

            if (!settings.UseMarketOrders)
            {
                logger.LogWarning(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Reason=OnlyMarketOrdersSupported",
                    correlationId, decisionId, symbol);
                return;
            }

            var cooldown = await tradeCooldownService.CheckCooldownAsync(symbol, settings.TradeCooldownSeconds, stoppingToken);
            if (cooldown.IsInCooldown)
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Reason=CooldownActive, RemainingSeconds={RemainingSeconds}, LastTradeAtUtc={LastTradeAtUtc}",
                    correlationId, decisionId, symbol, cooldown.RemainingSeconds, cooldown.LastTradeAtUtc);
                executionDesicion.IsInCooldown = cooldown.IsInCooldown;
                executionDesicion.CooldownRemainingSeconds = cooldown.RemainingSeconds;
                executionDesicion.CooldownLastTrade = cooldown.LastTradeAtUtc;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

            if (!await tradeIdempotencyService.TryRegisterDecisionAsync(decisionId, settings.IdempotencyWindowSeconds, stoppingToken))
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Reason=IdempotencyDuplicate",
                    correlationId, decisionId, symbol);
                executionDesicion.IdempotencyDuplicate = true;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

            var side = decision.Candidate?.Side ?? (decision.Action == TradeSignal.Buy ? OrderSide.BUY : OrderSide.SELL);
            var price = decision.Candidate?.Price ?? 0m;
            var riskResult = await riskManagementService.ValidateTrade(
                symbol,
                settings.Quantity,
                price,
                side,
                stoppingToken);

            executionDesicion.Side = side;
            executionDesicion.StopLossPrice = riskResult.StopLossPrice;
            executionDesicion.TakeProfitPrice = riskResult.TakeProfitPrice;
            executionDesicion.RiskIsAllowed = riskResult.IsAllowed;
            executionDesicion.RiskReason = riskResult.Reason;
            if (!riskResult.IsAllowed)
            {
                logger.LogWarning(
                    "DecisionWorker execution rejected by risk: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, Reason={Reason}",
                    correlationId, decisionId, symbol, side, riskResult.Reason);
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

            logger.LogInformation(
                "DecisionWorker risk approved: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, StopLossPrice={StopLossPrice}, TakeProfitPrice={TakeProfitPrice}",
                correlationId, decisionId, symbol, side, riskResult.StopLossPrice, riskResult.TakeProfitPrice);

            var executionResult = await tradeExecutionService.ExecuteMarketOrderAsync(
                new TradeExecutionRequest
                {
                    CorrelationId = correlationId,
                    DecisionId = decisionId,
                    Symbol = symbol,
                    Side = side,
                    Quantity = settings.Quantity
                },
                stoppingToken);

            if (executionResult.Success)
            {
                await tradeCooldownService.MarkTradeExecutedAsync(symbol, stoppingToken);
                logger.LogInformation(
                    "DecisionWorker execution succeeded: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}",
                    correlationId, decisionId, symbol, side, executionResult.LocalOrderId, executionResult.ExchangeOrderId);
                executionDesicion.ExecutionSuccess = executionResult.Success;
                executionDesicion.LocalOrderId = executionResult.LocalOrderId;
                executionDesicion.ExchangeOrderId = executionResult.ExchangeOrderId;
                await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
                return;
            }

            logger.LogWarning(
                "DecisionWorker execution failed: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, Error={Error}",
                correlationId, decisionId, symbol, side, executionResult.Error);
            executionDesicion.ExecutionError = executionResult.Error;
            await tradeExecutionDesicionsRepository.UpdateDesicionAsync(executionDesicion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "DecisionWorker symbol pipeline failed: CorrelationId={CorrelationId}, Symbol={Symbol}",
                correlationId,
                symbol);
        }
    }

    private DecisionWorkerSettings ReadSettings()
    {
        var intervalSeconds = Math.Max(1, configuration.GetValue<int?>("DecisionEngine:IntervalSeconds") ?? DefaultIntervalSeconds);
        var quantity = configuration.GetValue<decimal?>("DecisionEngine:Quantity") ?? DefaultQuantity;
        var executionEnabled = configuration.GetValue<bool?>("ExecutionSettings:Enabled") ?? false;
        var useMarketOrders = configuration.GetValue<bool?>("ExecutionSettings:UseMarketOrders") ?? true;
        var minConfidence = configuration.GetValue<decimal?>("DecisionEngine:MinConfidence")
                            ?? configuration.GetValue<decimal?>("DecisionEngine:MinConfidenceThreshold")
                            ?? DefaultMinConfidence;
        var cooldownSeconds = Math.Max(0,
            configuration.GetValue<int?>("Trading:CooldownSeconds")
            ?? configuration.GetValue<int?>("DecisionEngine:TradeCooldownSeconds")
            ?? DefaultTradeCooldownSeconds);
        var idempotencyWindowSeconds = Math.Max(10, configuration.GetValue<int?>("DecisionEngine:IdempotencyWindowSeconds") ?? DefaultIdempotencyWindowSeconds);

        var configuredSymbols = configuration.GetSection("DecisionEngine:Symbols").Get<string[]>() ?? [];
        var parsedSymbols = configuredSymbols
            .Select(x => Enum.TryParse<TradingSymbol>(x, true, out var symbol) ? symbol : (TradingSymbol?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        if (parsedSymbols.Count == 0)
        {
            var symbolText = configuration.GetValue<string>("DecisionEngine:Symbol") ?? DefaultSymbol;
            var symbol = Enum.TryParse<TradingSymbol>(symbolText, true, out var parsed) ? parsed : TradingSymbol.BTCUSDT;
            parsedSymbols.Add(symbol);
        }

        return new DecisionWorkerSettings
        {
            IntervalSeconds = intervalSeconds,
            Quantity = quantity,
            Symbols = parsedSymbols,
            ExecutionEnabled = executionEnabled,
            UseMarketOrders = useMarketOrders,
            MinConfidence = Math.Clamp(minConfidence, 0m, 1m),
            TradeCooldownSeconds = cooldownSeconds,
            IdempotencyWindowSeconds = idempotencyWindowSeconds
        };
    }

    private static string CreateDecisionId(TradingSymbol symbol, decimal quantity, TradingBot.Domain.Models.Decision.DecisionResult decision, int idempotencyWindowSeconds)
    {
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Math.Max(1, idempotencyWindowSeconds);
        var raw = $"{symbol}|{decision.Action}|{quantity:F8}|{decision.Candidate?.Price ?? 0m:F8}|{decision.Confidence:F4}|{bucket}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..32];
    }

    private sealed class DecisionWorkerSettings
    {
        public int IntervalSeconds { get; init; }
        public decimal Quantity { get; init; }
        public required IReadOnlyList<TradingSymbol> Symbols { get; init; }
        public bool ExecutionEnabled { get; init; }
        public bool UseMarketOrders { get; init; }
        public decimal MinConfidence { get; init; }
        public int TradeCooldownSeconds { get; init; }
        public int IdempotencyWindowSeconds { get; init; }
    }
}
