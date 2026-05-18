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
    public async Task BuyBelowEntryMinConfidence_IsBlocked()
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
            Confidence = 0.60m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal(0.70m, result.MinConfidence);
        Assert.Equal("Confidence below minimum threshold.", result.Reason);
    }

    [Fact]
    public async Task BuyAt0745_IsAllowed_WhenEntryMinConfidenceIs070()
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
            Confidence = 0.745m
        });

        Assert.True(result.IsAllowed);
        Assert.Equal(0.70m, result.MinConfidence);
    }

    [Fact]
    public async Task SellCloseLong_AtExitMinConfidence_IsAllowed()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinConfidence"] = "0.70",
            ["DecisionEngine:ExitMinConfidence"] = "0.45"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Sell,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Confidence = 0.45m
        });

        Assert.True(result.IsAllowed);
        Assert.Equal(0.45m, result.MinConfidence);
    }

    [Fact]
    public async Task SellCloseLong_BelowExitMinConfidence_IsBlocked()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinConfidence"] = "0.70",
            ["DecisionEngine:ExitMinConfidence"] = "0.45"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Sell,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Confidence = 0.44m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal(0.45m, result.MinConfidence);
    }

    [Fact]
    public async Task SellCloseLong_WithSignalFloor025_StillUsesExitMinConfidence045_ForBlocking()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinimumSignalConfidence"] = "0.25",
            ["DecisionEngine:MinConfidence"] = "0.70",
            ["DecisionEngine:ExitMinConfidence"] = "0.45",
            ["DecisionEngine:Strategies:MovingAverageCrossover:ExitMinConfidence"] = "0.45"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Sell,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Confidence = 0.30m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal(0.45m, result.MinConfidence);
    }

    [Fact]
    public async Task SellCloseLong_WithSignalFloor025_StillUsesExitMinConfidence045_ForPassing()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinimumSignalConfidence"] = "0.25",
            ["DecisionEngine:MinConfidence"] = "0.70",
            ["DecisionEngine:ExitMinConfidence"] = "0.45",
            ["DecisionEngine:Strategies:MovingAverageCrossover:ExitMinConfidence"] = "0.45"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Sell,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Confidence = 0.45m
        });

        Assert.True(result.IsAllowed);
        Assert.Equal(0.45m, result.MinConfidence);
    }

    [Fact]
    public async Task MissingExitMinConfidence_FallsBackToEntryMinConfidence()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinConfidence"] = "0.70"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Sell,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Confidence = 0.60m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal(0.70m, result.MinConfidence);
    }

    [Fact]
    public async Task StrategySpecificExitMinConfidence_OverridesGlobalExitMinConfidence()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinConfidence"] = "0.70",
            ["DecisionEngine:ExitMinConfidence"] = "0.50",
            ["DecisionEngine:Strategies:MovingAverageCrossover:ExitMinConfidence"] = "0.55"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Sell,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Confidence = 0.52m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal(0.55m, result.MinConfidence);
    }

    [Fact]
    public async Task AppsettingsExitMinConfidence_IsUsed_WhenPlatformHasNoRuntimeOverride()
    {
        var appSettings = new Dictionary<string, string?>
        {
            ["DecisionEngine:MinConfidence"] = "0.70",
            ["DecisionEngine:ExitMinConfidence"] = "0.45"
        };
        var platformSettings = new Dictionary<string, string?>
        {
            ["BaseURL"] = "https://testnet.binance.vision",
            ["Trading:NewOrder:api"] = "/api/v3/order"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(appSettings)
            .AddInMemoryCollection(platformSettings)
            .Build();

        var gate = new ConfidenceGate(config, NullLogger<ConfidenceGate>.Instance);
        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.ETHUSDT,
            Action = TradeSignal.Sell,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Confidence = 0.45m
        });

        Assert.True(result.IsAllowed);
        Assert.Equal(0.45m, result.MinConfidence);
    }

    [Fact]
    public async Task FuturesOpenShortSell_DoesNotUseExitMinConfidence()
    {
        var gate = CreateGate(new Dictionary<string, string?>
        {
            ["DecisionEngine:MinConfidence"] = "0.70",
            ["DecisionEngine:ExitMinConfidence"] = "0.50"
        });

        var result = await gate.EvaluateAsync(new ConfidenceGateRequest
        {
            StrategyName = "MovingAverageCrossover",
            Symbol = TradingSymbol.BNBUSDT,
            Action = TradeSignal.Sell,
            TradingMode = TradingMode.Futures,
            ExecutionIntent = TradeExecutionIntent.OpenShort,
            Confidence = 0.60m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal(0.70m, result.MinConfidence);
    }

    private static ConfidenceGate CreateGate(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new ConfidenceGate(config, NullLogger<ConfidenceGate>.Instance);
    }
}
