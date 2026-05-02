using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.BackgroundHostService.Services;

public class TradeDecisionService(
    IDecisionService decisionService,
    IConfiguration configuration,
    ILogger<TradeDecisionService> logger)
{
    private readonly TradingSymbol _defaultSymbol = GetDefaultSymbol(configuration);
    private readonly decimal _defaultQuantity = GetDefaultQuantity(configuration);

    public async Task<DecisionResult> MakeDecision(TradingSymbol symbol, decimal quantity, CancellationToken cancellationToken = default)
    {
        var result = await decisionService.DecideAsync(symbol, quantity, cancellationToken);
        var finalResult = NormalizeResultConfidence(result);

        logger.LogInformation(
            "TradeDecisionService result: Symbol={Symbol}, Action={Action}, Confidence={Confidence:F4}, Reason={Reason}, CandidateSide={CandidateSide}, CandidatePrice={CandidatePrice}, CandidateQuantity={CandidateQuantity}",
            symbol,
            finalResult.Action,
            finalResult.Confidence,
            finalResult.Reason,
            finalResult.Candidate?.Side,
            finalResult.Candidate?.Price,
            finalResult.Candidate?.Quantity);
        return finalResult;
    }

    public Task<DecisionResult> MakeDecision(CancellationToken cancellationToken = default)
    {
        return MakeDecision(_defaultSymbol, _defaultQuantity, cancellationToken);
    }

    [Obsolete("Use MakeDecision instead.")]
    public Task<DecisionResult> MakeDesicion(TradingSymbol symbol, decimal quantity, CancellationToken cancellationToken = default)
    {
        return MakeDecision(symbol, quantity, cancellationToken);
    }

    [Obsolete("Use MakeDecision instead.")]
    public Task<DecisionResult> MakeDesicion(CancellationToken cancellationToken = default)
    {
        return MakeDecision(cancellationToken);
    }

    private static DecisionResult NormalizeResultConfidence(DecisionResult result)
    {
        var normalizedConfidence = Math.Clamp(result.Confidence, 0m, 1m);
        if (normalizedConfidence == result.Confidence)
            return result;

        var normalized = new DecisionResult();
        var properties = typeof(DecisionResult).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            if (!property.CanRead || !property.CanWrite)
                continue;

            var value = property.Name == nameof(DecisionResult.Confidence)
                ? normalizedConfidence
                : property.GetValue(result);
            property.SetValue(normalized, value);
        }

        return normalized;
    }

    private static TradingSymbol GetDefaultSymbol(IConfiguration configuration)
    {
        var configured = configuration["TradingDefaults:Symbol"];
        return Enum.TryParse<TradingSymbol>(configured, true, out var symbol)
            ? symbol
            : TradingSymbol.BTCUSDT;
    }

    private static decimal GetDefaultQuantity(IConfiguration configuration)
    {
        var configured = configuration["TradingDefaults:Quantity"];
        return decimal.TryParse(configured, out var quantity) && quantity > 0m
            ? quantity
            : 0.001m;
    }
}

[Obsolete("Use TradeDecisionService instead.")]
public class TradeDesicionService(
    IDecisionService decisionService,
    IConfiguration configuration,
    ILogger<TradeDecisionService> logger)
    : TradeDecisionService(decisionService, configuration, logger)
{
}
