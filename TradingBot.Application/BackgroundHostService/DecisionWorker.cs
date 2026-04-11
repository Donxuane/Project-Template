using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Services;

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var correlationId = Guid.NewGuid().ToString("N");
                foreach (var symbol in settings.Symbols)
                {
                    await ProcessSymbolAsync(symbol, settings, correlationId, stoppingToken);
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
        }

        logger.LogInformation("DecisionWorker stopped.");
    }

    private async Task ProcessSymbolAsync(
        TradingSymbol symbol,
        DecisionWorkerSettings settings,
        string correlationId,
        CancellationToken stoppingToken)
    {
        try
        {
            var decision = await tradeDecisionService.MakeDesicion(symbol, settings.Quantity, stoppingToken);
            var decisionId = CreateDecisionId(symbol, settings.Quantity, decision, settings.IdempotencyWindowSeconds);

            logger.LogInformation(
                "DecisionWorker decision generated: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Action={Action}, Confidence={Confidence:F4}, Reason={Reason}",
                correlationId, decisionId, symbol, decision.Action, decision.Confidence, decision.Reason);

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
                return;
            }

            if (!await tradeIdempotencyService.TryRegisterDecisionAsync(decisionId, settings.IdempotencyWindowSeconds, stoppingToken))
            {
                logger.LogInformation(
                    "DecisionWorker execution skipped: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Reason=IdempotencyDuplicate",
                    correlationId, decisionId, symbol);
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

            if (!riskResult.IsAllowed)
            {
                logger.LogWarning(
                    "DecisionWorker execution rejected by risk: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, Reason={Reason}",
                    correlationId, decisionId, symbol, side, riskResult.Reason);
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
                return;
            }

            logger.LogWarning(
                "DecisionWorker execution failed: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, Error={Error}",
                correlationId, decisionId, symbol, side, executionResult.Error);
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
        return Convert.ToHexString(bytes)[..16];
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
