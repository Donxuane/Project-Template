using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService.Services;

public class ConfidenceGate(
    IConfiguration configuration,
    ILogger<ConfidenceGate> logger) : IConfidenceGate
{
    private const decimal DefaultMinConfidence = 0.70m;

    public Task<ConfidenceGateResult> EvaluateAsync(ConfidenceGateRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedStrategyName = NormalizeStrategyName(request.StrategyName);
        var strategyKey = $"DecisionEngine:Strategies:{normalizedStrategyName}:MinConfidence";
        var strategySpecific = configuration.GetValue<decimal?>(strategyKey);
        var global = configuration.GetValue<decimal?>("DecisionEngine:MinConfidence");
        var minConfidence = Math.Clamp(strategySpecific ?? global ?? DefaultMinConfidence, 0m, 1m);

        var allowed = request.Confidence >= minConfidence;
        var reason = allowed
            ? "Confidence gate passed."
            : "Confidence below minimum threshold.";

        logger.LogInformation(
            "ConfidenceGate evaluated: StrategyName={StrategyName}, Symbol={Symbol}, Action={Action}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Confidence={Confidence:F4}, MinConfidence={MinConfidence:F4}, Allowed={Allowed}, Reason={Reason}",
            normalizedStrategyName,
            request.Symbol,
            request.Action,
            request.TradingMode,
            request.ExecutionIntent,
            request.Confidence,
            minConfidence,
            allowed,
            reason);

        return Task.FromResult(new ConfidenceGateResult
        {
            IsAllowed = allowed,
            Reason = reason,
            StrategyName = normalizedStrategyName,
            Symbol = request.Symbol,
            Action = request.Action,
            ExecutionIntent = request.ExecutionIntent,
            Confidence = request.Confidence,
            MinConfidence = minConfidence
        });
    }

    private static string NormalizeStrategyName(string strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
            return "Unknown";

        var trimmed = strategyName.Trim();
        return trimmed.EndsWith("Strategy", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^"Strategy".Length]
            : trimmed;
    }
}
