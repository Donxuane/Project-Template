using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.Trading;
using Xunit;

namespace TradingBot.Application.Tests;

public class PositionReconciliationServiceTests
{
    private readonly PositionReconciliationService _service = new();

    [Fact]
    public void MatchWithinTolerance_IsMatched()
    {
        var open = new[] { CreateOpenPosition(TradingSymbol.BNBUSDT, 0.01m) };
        var balances = new[] { CreateBalance("BNB", 0.009999995m, 0m) };
        var snapshots = CreateSnapshots(("BNB", 0.009999995m, 0m, DateTime.UtcNow));

        var result = _service.EvaluateSpot(open, Array.Empty<Position>(), balances, snapshots, 0.00000001m, 1, TimeSpan.FromMinutes(5));

        Assert.Contains(result, x => x.Symbol == TradingSymbol.BNBUSDT && x.IsMatched);
    }

    [Fact]
    public void LocalGreaterThanExchange_CreatesMismatch()
    {
        var open = new[] { CreateOpenPosition(TradingSymbol.BNBUSDT, 0.02m) };
        var balances = new[] { CreateBalance("BNB", 0.01m, 0m) };
        var snapshots = CreateSnapshots(("BNB", 0.01m, 0m, DateTime.UtcNow));

        var result = _service.EvaluateSpot(open, Array.Empty<Position>(), balances, snapshots, 0.00000001m, 1, TimeSpan.FromMinutes(5));

        Assert.Contains(result, x =>
            x.Symbol == TradingSymbol.BNBUSDT
            && !x.IsMatched
            && x.Reason.Contains("differs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExchangeBalanceWithoutLocalPosition_CreatesMismatch()
    {
        var balances = new[] { CreateBalance("BNB", 0.01m, 0m) };
        var snapshots = CreateSnapshots(("BNB", 0.01m, 0m, DateTime.UtcNow));

        var result = _service.EvaluateSpot(Array.Empty<Position>(), Array.Empty<Position>(), balances, snapshots, 0.00000001m, 1, TimeSpan.FromMinutes(5));

        Assert.Contains(result, x =>
            x.Symbol == TradingSymbol.BNBUSDT
            && !x.IsMatched
            && x.Reason.Contains("no local open position", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalOpenWithZeroExchangeBalance_CreatesMismatch()
    {
        var open = new[] { CreateOpenPosition(TradingSymbol.BNBUSDT, 0.01m) };
        var balances = new[] { CreateBalance("BNB", 0m, 0m) };
        var snapshots = CreateSnapshots(("BNB", 0m, 0m, DateTime.UtcNow));

        var result = _service.EvaluateSpot(open, Array.Empty<Position>(), balances, snapshots, 0.00000001m, 1, TimeSpan.FromMinutes(5));

        Assert.Contains(result, x =>
            x.Symbol == TradingSymbol.BNBUSDT
            && !x.IsMatched
            && x.Reason.Contains("zero or below tolerance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExchangeTotal_UsesFreePlusLocked()
    {
        var open = new[] { CreateOpenPosition(TradingSymbol.BNBUSDT, 0.01m) };
        var balances = new[] { CreateBalance("BNB", 0.005m, 0.005m) };
        var snapshots = CreateSnapshots(("BNB", 0.005m, 0.005m, DateTime.UtcNow));

        var result = _service.EvaluateSpot(open, Array.Empty<Position>(), balances, snapshots, 0.00000001m, 1, TimeSpan.FromMinutes(5));
        var bnb = Assert.Single(result.Where(x => x.Symbol == TradingSymbol.BNBUSDT && x.IsMatched));

        Assert.Equal(0.005m, bnb.ExchangeFree);
        Assert.Equal(0.005m, bnb.ExchangeLocked);
        Assert.Equal(0.01m, bnb.ExchangeTotal);
    }

    [Fact]
    public void Tolerance_IsRespected()
    {
        var open = new[] { CreateOpenPosition(TradingSymbol.BNBUSDT, 1.000000005m) };
        var balances = new[] { CreateBalance("BNB", 1m, 0m) };
        var snapshots = CreateSnapshots(("BNB", 1m, 0m, DateTime.UtcNow));

        var matched = _service.EvaluateSpot(open, Array.Empty<Position>(), balances, snapshots, 0.00000001m, 1, TimeSpan.FromMinutes(5));
        Assert.Contains(matched, x => x.Symbol == TradingSymbol.BNBUSDT && x.IsMatched);

        var mismatched = _service.EvaluateSpot(open, Array.Empty<Position>(), balances, snapshots, 0.000000001m, 1, TimeSpan.FromMinutes(5));
        Assert.Contains(mismatched, x => x.Symbol == TradingSymbol.BNBUSDT && !x.IsMatched);
    }

    [Fact]
    public void Reconciliation_IsReadOnly_DoesNotMutatePositions()
    {
        var position = CreateOpenPosition(TradingSymbol.BNBUSDT, 0.01m);
        var open = new[] { position };
        var balances = new[] { CreateBalance("BNB", 0.005m, 0m) };
        var snapshots = CreateSnapshots(("BNB", 0.005m, 0m, DateTime.UtcNow));

        _ = _service.EvaluateSpot(open, Array.Empty<Position>(), balances, snapshots, 0.00000001m, 1, TimeSpan.FromMinutes(5));

        Assert.True(position.IsOpen);
        Assert.Equal(0.01m, position.Quantity);
    }

    private static Position CreateOpenPosition(TradingSymbol symbol, decimal quantity)
    {
        return new Position
        {
            Id = 1,
            Symbol = symbol,
            Side = OrderSide.BUY,
            Quantity = quantity,
            AveragePrice = 100m,
            IsOpen = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static Balance CreateBalance(string asset, decimal free, decimal locked)
    {
        return new Balance
        {
            Asset = asset,
            Free = free.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Locked = locked.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static IReadOnlyDictionary<string, BalanceSnapshot> CreateSnapshots(params (string Asset, decimal Free, decimal Locked, DateTime UpdatedAt)[] rows)
    {
        return rows.ToDictionary(
            x => x.Asset,
            x => new BalanceSnapshot
            {
                Asset = x.Asset,
                Free = x.Free,
                Locked = x.Locked,
                UpdatedAt = x.UpdatedAt,
                CreatedAt = x.UpdatedAt
            },
            StringComparer.OrdinalIgnoreCase);
    }
}
