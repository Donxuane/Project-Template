using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.BackgroundHostService.Services;

public class TradeDesicionService(IDecisionService decisionService, ILogger<TradeDesicionService> logger)
{
    public async Task<DecisionResult> MakeDesicion(TradingSymbol symbol, decimal quantity, CancellationToken cancellationToken = default)
    {
        var result = await decisionService.DecideAsync(symbol, quantity, cancellationToken);
        var normalizedConfidence = NormalizeConfidence(result.Confidence);
        var finalResult = new DecisionResult
        {
            Action = result.Action,
            Reason = result.Reason,
            Candidate = result.Candidate,
            Confidence = normalizedConfidence
        };

        logger.LogInformation(
            "TradeDesicionService result: Symbol={Symbol}, Action={Action}, Confidence={Confidence:F4}, Reason={Reason}",
            symbol, finalResult.Action, finalResult.Confidence, finalResult.Reason);
        return finalResult;
    }

    public Task<DecisionResult> MakeDesicion(CancellationToken cancellationToken = default)
    {
        // Backward-compatible overload for existing callers.
        return MakeDesicion(TradingSymbol.BTCUSDT, 0.001m, cancellationToken);
    }

    private static decimal NormalizeConfidence(decimal confidence)
    {
        if (confidence < 0m || confidence > 1m)
            return 1.0m;

        return confidence;
    }
}
