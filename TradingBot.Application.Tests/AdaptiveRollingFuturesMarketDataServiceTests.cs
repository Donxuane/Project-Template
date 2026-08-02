using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.SpotFuturesCrossMarket;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using Xunit;

namespace TradingBot.Application.Tests;

public class AdaptiveRollingFuturesMarketDataServiceTests
{
    [Fact]
    public void GetSnapshot_MissingAggTrade_RemainsFreshWithFreshCriticalStreams()
    {
        var now = DateTime.UtcNow;
        var service = CreateServiceWithState(
            bookTickerReceiptUtc: now,
            depthReceiptUtc: now,
            markPriceReceiptUtc: now,
            aggTradeReceiptUtc: null);

        var snapshot = service.GetSnapshot(
            TradingSymbol.BTCUSDT,
            OrderSide.BUY,
            closeQuantity: 0m,
            Settings());

        Assert.True(snapshot.IsFresh);
        Assert.Null(snapshot.DegradedReason);
        Assert.Equal(0m, snapshot.AggressiveFlowImbalance);
    }

    [Fact]
    public void GetSnapshot_StaleDepth_RemainsDegraded()
    {
        var now = DateTime.UtcNow;
        var service = CreateServiceWithState(
            bookTickerReceiptUtc: now,
            depthReceiptUtc: now.AddSeconds(-10),
            markPriceReceiptUtc: now,
            aggTradeReceiptUtc: now);

        var snapshot = service.GetSnapshot(
            TradingSymbol.BTCUSDT,
            OrderSide.BUY,
            closeQuantity: 0m,
            Settings());

        Assert.False(snapshot.IsFresh);
        Assert.Equal("MarketDataStale", snapshot.DegradedReason);
        Assert.True(snapshot.MarketDataAgeMs > Settings().MarketDataMaxAgeMs);
    }

    private static AdaptiveRollingFuturesMarketDataService CreateServiceWithState(
        DateTime bookTickerReceiptUtc,
        DateTime depthReceiptUtc,
        DateTime markPriceReceiptUtc,
        DateTime? aggTradeReceiptUtc)
    {
        var service = new AdaptiveRollingFuturesMarketDataService(
            NullLogger<AdaptiveRollingFuturesMarketDataService>.Instance);
        var serviceType = typeof(AdaptiveRollingFuturesMarketDataService);
        var stateType = serviceType.GetNestedType("SymbolSocketState", BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("SymbolSocketState was not found.");
        var state = Activator.CreateInstance(
                        stateType,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        args: [TradingSymbol.BTCUSDT],
                        culture: null)
                    ?? throw new InvalidOperationException("SymbolSocketState could not be created.");

        Set(stateType, state, "Connected", true);
        Set(stateType, state, "BestBidPrice", 100m);
        Set(stateType, state, "BestBidQuantity", 2m);
        Set(stateType, state, "BestAskPrice", 101m);
        Set(stateType, state, "BestAskQuantity", 2m);
        Set(stateType, state, "MarkPrice", 100.5m);
        Set(stateType, state, "IndexPrice", 100.4m);
        Set(stateType, state, "Bids", new List<OrderBookLevel> { new(100m, 10m) });
        Set(stateType, state, "Asks", new List<OrderBookLevel> { new(101m, 10m) });
        Set(stateType, state, "LastBookTickerLocalReceiptUtc", bookTickerReceiptUtc);
        Set(stateType, state, "LastDepthLocalReceiptUtc", depthReceiptUtc);
        Set(stateType, state, "LastAggTradeLocalReceiptUtc", aggTradeReceiptUtc);
        Set(stateType, state, "LastMarkPriceLocalReceiptUtc", markPriceReceiptUtc);
        Set(stateType, state, "LastBookTickerEventTimeUtc", bookTickerReceiptUtc);
        Set(stateType, state, "LastDepthEventTimeUtc", depthReceiptUtc);
        Set(stateType, state, "LastAggTradeEventTimeUtc", aggTradeReceiptUtc);
        Set(stateType, state, "LastMarkPriceEventTimeUtc", markPriceReceiptUtc);

        var symbolsField = serviceType.GetField("_symbols", BindingFlags.Instance | BindingFlags.NonPublic)
                           ?? throw new InvalidOperationException("Symbol state dictionary was not found.");
        var symbols = symbolsField.GetValue(service)
                      ?? throw new InvalidOperationException("Symbol state dictionary was null.");
        var added = symbols.GetType().GetMethod("TryAdd")?.Invoke(
            symbols,
            [TradingSymbol.BTCUSDT, state]);
        Assert.Equal(true, added);

        return service;
    }

    private static AdaptiveRollingProfitExitV1Settings Settings()
        => new()
        {
            MarketDataMaxAgeMs = 3000,
            StreamLatencyDegradedMs = 2000,
            FlowWindowSeconds = 60,
            VelocityWindowSeconds = 60
        };

    private static void Set(Type stateType, object state, string propertyName, object? value)
    {
        var property = stateType.GetProperty(
                           propertyName,
                           BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? throw new InvalidOperationException($"{propertyName} was not found.");
        property.SetValue(state, value);
    }
}
