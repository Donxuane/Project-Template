using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services;
using Xunit;

namespace TradingBot.Application.Tests;

public class ConfidenceGateTests
{
    [Fact]
    public async Task AboveGlobalThreshold_IsAllowed()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinConfidence"] = "0.70"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Buy,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Confidence = 0.75m
        });

        Assert.True(result.IsAllowed);
        Assert.Equal(0.70m, result.MinConfidence);
    }

    [Fact]
    public async Task BelowGlobalThreshold_IsBlocked()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinConfidence"] = "0.70"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Buy,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Confidence = 0.65m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal("Confidence below minimum threshold.", result.Reason);
    }

    [Fact]
    public async Task StrategySpecificThreshold_OverridesGlobal()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinConfidence"] = "0.70",
            ["DecisionEngine:Strategies:MovingAverageCrossover:MinConfidence"] = "0.80"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Buy,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Confidence = 0.75m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal(0.80m, result.MinConfidence);
    }

    private static ConfidenceGate CreateGate(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new ConfidenceGate(config, NullLogger<ConfidenceGate>.Instance);
    }
}
