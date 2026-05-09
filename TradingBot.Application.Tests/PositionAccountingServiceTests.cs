using TradingBot.Application.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models.Trading;
using Xunit;

namespace TradingBot.Application.Tests;

public class PositionAccountingServiceTests
{
    private readonly PositionAccountingService _service = new();

    [Fact]
    public void OpenLongPosition()
    {
        var order = CreateOrder(OrderSide.BUY);
        var executedAt = DateTime.UtcNow.AddSeconds(-12);
        var trades = new[] { CreateTrade(1, OrderSide.BUY, 100_000m, 1m, executedAt: executedAt) };

        var result = _service.ApplyTrades(null, order, trades);

        Assert.Equal(1m, result.Position.Quantity);
        Assert.Equal(OrderSide.BUY, result.Position.Side);
        Assert.Equal(100_000m, result.Position.AveragePrice);
        Assert.True(result.PositionOpened);
        Assert.False(result.PositionClosed);
        Assert.Equal(executedAt, result.Position.OpenedAt);
    }

    [Fact]
    public void AddToLongUpdatesWeightedAverage()
    {
        var current = CreatePosition(1m, 100_000m);
        var order = CreateOrder(OrderSide.BUY);
        var trades = new[] { CreateTrade(2, OrderSide.BUY, 110_000m, 1m) };

        var result = _service.ApplyTrades(current, order, trades);

        Assert.Equal(2m, result.Position.Quantity);
        Assert.Equal(105_000m, result.Position.AveragePrice);
    }

    [Fact]
    public void PartiallyCloseLongKeepsAverageAndRealizesPnl()
    {
        var current = CreatePosition(1m, 100_000m);
        var order = CreateOrder(OrderSide.SELL);
        var trades = new[] { CreateTrade(3, OrderSide.SELL, 101_000m, 0.4m) };

        var result = _service.ApplyTrades(current, order, trades);

        Assert.Equal(0.6m, result.Position.Quantity);
        Assert.Equal(100_000m, result.Position.AveragePrice);
        Assert.Equal(400m, result.RealizedPnlDelta);
    }

    [Fact]
    public void FullyCloseLong()
    {
        var current = CreatePosition(1m, 100_000m);
        var order = CreateOrder(OrderSide.SELL);
        var trades = new[] { CreateTrade(4, OrderSide.SELL, 101_000m, 1m) };

        var result = _service.ApplyTrades(current, order, trades);

        Assert.Equal(0m, result.Position.Quantity);
        Assert.False(result.Position.IsOpen);
        Assert.True(result.PositionClosed);
        Assert.Equal(1_000m, result.RealizedPnlDelta);
    }

    [Fact]
    public void FullyCloseLong_UsesExecutionPriceAsExitPrice_AndClosesQuantityToZero()
    {
        var current = CreatePosition(0.01m, 617.24m, TradingSymbol.BNBUSDT);
        var order = CreateOrder(OrderSide.SELL, TradingSymbol.BNBUSDT);
        var executedAt = DateTime.UtcNow.AddSeconds(-3);
        var trades = new[] { CreateTrade(40, OrderSide.SELL, 618.01m, 0.01m, executedAt: executedAt) };

        var result = _service.ApplyTrades(current, order, trades);

        Assert.False(result.Position.IsOpen);
        Assert.Equal(0m, result.Position.Quantity);
        Assert.Equal(618.01m, result.Position.ExitPrice);
        Assert.Equal(executedAt, result.Position.ClosedAt);
        Assert.False(result.Position.IsClosing);
    }

    [Fact]
    public void SellMoreThanOpenQuantity_DoesNotFlipNegative_ClosesSafely()
    {
        var current = CreatePosition(1m, 100_000m);
        var order = CreateOrder(OrderSide.SELL);
        var trades = new[] { CreateTrade(5, OrderSide.SELL, 101_000m, 2m) };

        var result = _service.ApplyTrades(current, order, trades);

        Assert.Equal(0m, result.Position.Quantity);
        Assert.False(result.Position.IsOpen);
        Assert.False(result.PositionFlipped);
        Assert.Equal(1_000m, result.RealizedPnlDelta);
    }

    [Fact]
    public void SellWithoutOpenLong_IsSkippedSafely()
    {
        var order = CreateOrder(OrderSide.SELL);
        var trades = new[] { CreateTrade(6, OrderSide.SELL, 100_000m, 1m) };

        var result = _service.ApplyTrades(null, order, trades);

        Assert.Equal(0m, result.Position.Quantity);
        Assert.False(result.Position.IsOpen);
        Assert.Equal(0, result.ProcessedTradeCount);
    }

    [Fact]
    public void DuplicateCloseDoesNotMakeQuantityNegative()
    {
        var current = CreatePosition(1m, 100_000m);
        var order = CreateOrder(OrderSide.SELL);
        var closeTrade = CreateTrade(7, OrderSide.SELL, 101_000m, 1m);
        var duplicateCloseTrade = CreateTrade(8, OrderSide.SELL, 101_000m, 1m);

        var first = _service.ApplyTrades(current, order, new[] { closeTrade });
        var second = _service.ApplyTrades(first.Position, order, new[] { duplicateCloseTrade });

        Assert.Equal(0m, second.Position.Quantity);
        Assert.False(second.Position.IsOpen);
        Assert.Equal(0m, second.RealizedPnlDelta);
    }

    [Fact]
    public void PartialCloseExample_RealizesOnlyClosedQuantity()
    {
        var current = CreatePosition(0.03m, 623m, TradingSymbol.BNBUSDT);
        var order = CreateOrder(OrderSide.SELL, TradingSymbol.BNBUSDT);
        var trades = new[] { CreateTrade(8, OrderSide.SELL, 625m, 0.01m) };

        var result = _service.ApplyTrades(current, order, trades);

        Assert.Equal(0.02m, result.Position.Quantity);
        Assert.True(result.Position.IsOpen);
        Assert.Equal(623m, result.Position.AveragePrice);
        Assert.Equal(0.02m, result.RealizedPnlDelta);
        Assert.False(result.Position.IsClosing);
    }

    [Fact]
    public void FullCloseExample_ClosesPositionAndSetsClosedAt()
    {
        var current = CreatePosition(0.03m, 623m, TradingSymbol.BNBUSDT);
        var order = CreateOrder(OrderSide.SELL, TradingSymbol.BNBUSDT);
        var trades = new[] { CreateTrade(9, OrderSide.SELL, 625m, 0.03m) };

        var result = _service.ApplyTrades(current, order, trades);

        Assert.Equal(0m, result.Position.Quantity);
        Assert.False(result.Position.IsOpen);
        Assert.True(result.PositionClosed);
        Assert.NotNull(result.Position.ClosedAt);
        Assert.Equal(0.06m, result.RealizedPnlDelta);
    }

    [Fact]
    public void QuoteFeesReduceRealizedPnl()
    {
        var current = CreatePosition(1m, 100_000m);
        var order = CreateOrder(OrderSide.SELL, TradingSymbol.BTCUSDT);
        var trades = new[]
        {
            CreateTrade(11, OrderSide.SELL, 101_000m, 1m, fee: 5m, feeAsset: "USDT")
        };

        var result = _service.ApplyTrades(current, order, trades);

        Assert.Equal(995m, result.RealizedPnlDelta);
        Assert.Equal(5m, result.FeeDelta);
    }

    [Fact]
    public void BaseAssetBuyFee_IsNotSubtractedAsQuoteFee()
    {
        var order = CreateOrder(OrderSide.BUY, TradingSymbol.BNBUSDT);
        var trades = new[]
        {
            CreateTrade(31, OrderSide.BUY, 600m, 0.01m, fee: 0.00001m, feeAsset: "BNB")
        };

        var result = _service.ApplyTrades(null, order, trades);

        Assert.Equal(0m, result.FeeDelta);
        Assert.Equal(0m, result.RealizedPnlDelta);
        Assert.True(result.Position.IsOpen);
    }

    [Fact]
    public void DuplicateTradeIsNotCountedTwice()
    {
        var order = CreateOrder(OrderSide.BUY);
        var trade = CreateTrade(12, OrderSide.BUY, 100_000m, 1m);
        var duplicate = CreateTrade(12, OrderSide.BUY, 100_000m, 1m);

        var result = _service.ApplyTrades(null, order, new[] { trade, duplicate });

        Assert.Equal(1, result.ProcessedTradeCount);
        Assert.Equal(1m, result.Position.Quantity);
    }

    [Fact]
    public void EmptyTradesReturnsSafeResult()
    {
        var result = _service.ApplyTrades(null, CreateOrder(OrderSide.BUY), Array.Empty<TradeExecution>());

        Assert.Equal(0, result.ProcessedTradeCount);
        Assert.Contains("No trades found", result.Reason);
    }

    [Fact]
    public void AverageDoesNotChangeOnPartialClose()
    {
        var current = CreatePosition(2m, 100_000m);
        var result = _service.ApplyTrades(
            current,
            CreateOrder(OrderSide.SELL),
            new[] { CreateTrade(13, OrderSide.SELL, 101_000m, 0.5m) });

        Assert.Equal(100_000m, result.Position.AveragePrice);
        Assert.Equal(1.5m, result.Position.Quantity);
    }

    private static Order CreateOrder(OrderSide side, TradingSymbol symbol = TradingSymbol.BTCUSDT)
    {
        return new Order
        {
            Id = 1,
            Symbol = symbol,
            Side = side,
            Quantity = 1m,
            Price = 100_000m
        };
    }

    private static Position CreatePosition(decimal quantity, decimal averagePrice, TradingSymbol symbol = TradingSymbol.BTCUSDT)
    {
        return new Position
        {
            Id = 7,
            Symbol = symbol,
            Quantity = quantity,
            AveragePrice = averagePrice,
            Side = quantity >= 0m ? OrderSide.BUY : OrderSide.SELL,
            IsOpen = quantity != 0m,
            RealizedPnl = 0m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static TradeExecution CreateTrade(
        long exchangeTradeId,
        OrderSide side,
        decimal price,
        decimal quantity,
        decimal fee = 0m,
        string? feeAsset = null,
        DateTime? executedAt = null)
    {
        return new TradeExecution
        {
            Id = exchangeTradeId,
            OrderId = 1,
            ExchangeOrderId = 1000,
            ExchangeTradeId = exchangeTradeId,
            Symbol = TradingSymbol.BTCUSDT,
            Side = side,
            Price = price,
            Quantity = quantity,
            Fee = fee,
            FeeAsset = feeAsset,
            ExecutedAt = executedAt ?? DateTime.UtcNow
        };
    }
}
