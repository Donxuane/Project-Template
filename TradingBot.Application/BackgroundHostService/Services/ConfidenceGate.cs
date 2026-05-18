using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Shared.Configuration;

namespace TradingBot.Application.BackgroundHostService.Services;

public class ConfidenceGate(
    IConfiguration configuration,
    ILogger<ConfidenceGate> logger) : IConfidenceGate
{
    public Task<ConfidenceGateResult> EvaluateAsync(ConfidenceGateRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedStrategyName = NormalizeStrategyName(request.StrategyName);
        var thresholdResolution = RuntimeTradingConfigResolver.ResolveConfidenceThreshold(
            configuration,
            normalizedStrategyName,
            request.Action.ToString(),
            request.TradingMode.ToString(),
            request.ExecutionIntent.ToString());
        var minConfidence = Math.Clamp(thresholdResolution.MinConfidence, 0m, 1m);

        var allowed = request.Confidence >= minConfidence;
        var reason = allowed
            ? "Confidence gate passed."
            : "Confidence below minimum threshold.";

        logger.LogInformation(
            "ConfidenceGate evaluated: StrategyName={StrategyName}, Symbol={Symbol}, Action={Action}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Confidence={Confidence:F4}, MinConfidence={MinConfidence:F4}, ThresholdKind={ThresholdKind}, ThresholdSource={ThresholdSource}, Allowed={Allowed}, Reason={Reason}",
            normalizedStrategyName,
            request.Symbol,
            request.Action,
            request.TradingMode,
            request.ExecutionIntent,
            request.Confidence,
            minConfidence,
            thresholdResolution.ThresholdKind,
            thresholdResolution.ThresholdSource,
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
