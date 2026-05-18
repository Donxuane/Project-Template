using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class DecisionWorkerSymbolQuantityTests
{
    [Fact]
    public void ResolveQuantity_UsesSymbolSpecificQuantity_WhenConfigured()
    {
        var worker = CreateWorker(new Dictionary<string, string?>
        {
            ["DecisionEngine:Quantity"] = "0.01",
            ["DecisionEngine:Symbols:0"] = "BNBUSDT",
            ["DecisionEngine:SymbolQuantities:BNBUSDT"] = "0.02"
        });

        var settings = ReadSettings(worker);
        var quantity = ResolveQuantity(worker, TradingSymbol.BNBUSDT, settings);

        Assert.Equal(0.02m, quantity);
    }

    [Fact]
    public void ResolveQuantity_FallsBackToGlobalQuantity_WhenSymbolOverrideMissing()
    {
        var worker = CreateWorker(new Dictionary<string, string?>
        {
            ["DecisionEngine:Quantity"] = "0.01",
            ["DecisionEngine:Symbols:0"] = "BNBUSDT",
            ["DecisionEngine:SymbolQuantities:ETHUSDT"] = "0.02"
        });

        var settings = ReadSettings(worker);
        var quantity = ResolveQuantity(worker, TradingSymbol.BNBUSDT, settings);

        Assert.Equal(0.01m, quantity);
    }

    [Fact]
    public void ReadSettings_IgnoresInvalidAndNegativeSymbolQuantities()
    {
        var worker = CreateWorker(new Dictionary<string, string?>
        {
            ["DecisionEngine:Quantity"] = "0.01",
            ["DecisionEngine:Symbols:0"] = "BNBUSDT",
            ["DecisionEngine:SymbolQuantities:BNBUSDT"] = "-0.5",
            ["DecisionEngine:SymbolQuantities:ETHUSDT"] = "invalid",
            ["DecisionEngine:SymbolQuantities:SOLUSDT"] = "0.05",
            ["DecisionEngine:SymbolQuantities:NOT_A_SYMBOL"] = "1.0"
        });

        var settings = ReadSettings(worker);
        var symbolQuantities = ReadSymbolQuantities(settings);

        Assert.False(symbolQuantities.ContainsKey(TradingSymbol.BNBUSDT));
        Assert.False(symbolQuantities.ContainsKey(TradingSymbol.ETHUSDT));
        Assert.True(symbolQuantities.ContainsKey(TradingSymbol.SOLUSDT));
        Assert.Equal(0.05m, symbolQuantities[TradingSymbol.SOLUSDT]);
    }

    [Fact]
    public void ResolveQuantity_CanReturnDifferentValuesForDifferentSymbols()
    {
        var worker = CreateWorker(new Dictionary<string, string?>
        {
            ["DecisionEngine:Quantity"] = "0.01",
            ["DecisionEngine:Symbols:0"] = "BNBUSDT",
            ["DecisionEngine:Symbols:1"] = "SOLUSDT",
            ["DecisionEngine:SymbolQuantities:BNBUSDT"] = "0.01",
            ["DecisionEngine:SymbolQuantities:SOLUSDT"] = "0.05"
        });

        var settings = ReadSettings(worker);

        var bnbQty = ResolveQuantity(worker, TradingSymbol.BNBUSDT, settings);
        var solQty = ResolveQuantity(worker, TradingSymbol.SOLUSDT, settings);

        Assert.Equal(0.01m, bnbQty);
        Assert.Equal(0.05m, solQty);
    }

    private static DecisionWorker CreateWorker(IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new DecisionWorker(
            new DummyScopeFactory(),
            configuration,
            NullLogger<DecisionWorker>.Instance);
    }

    private static object ReadSettings(DecisionWorker worker)
    {
        var method = typeof(DecisionWorker).GetMethod("ReadSettings", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(worker, null)!;
    }

    private static decimal ResolveQuantity(DecisionWorker worker, TradingSymbol symbol, object settings)
    {
        var method = typeof(DecisionWorker).GetMethod("ResolveQuantityForSymbol", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (decimal)method!.Invoke(null, [symbol, settings])!;
    }

    private static IReadOnlyDictionary<TradingSymbol, decimal> ReadSymbolQuantities(object settings)
    {
        var property = settings.GetType().GetProperty("SymbolQuantities", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return (IReadOnlyDictionary<TradingSymbol, decimal>)property!.GetValue(settings)!;
    }

    private sealed class DummyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }
}
