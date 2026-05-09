using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;
using Xunit;

namespace TradingBot.Application.Tests;

public class TradeAnalyticsServiceTests
{
    [Fact]
    public async Task GetSummary_IncludesOppositeSignalAndNullExitReason_AndExcludesInvalidRows()
    {
        var positions = new[]
        {
            CreateClosedPosition(
                id: 1,
                pnl: 10m,
                closedAt: DateTime.UtcNow.AddMinutes(-5),
                exitPrice: 618.01m,
                exitReason: PositionExitReason.OppositeSignal,
                isOpen: false),
            CreateClosedPosition(
                id: 2,
                pnl: -4m,
                closedAt: DateTime.UtcNow.AddMinutes(-4),
                exitPrice: 617.90m,
                exitReason: null,
                isOpen: false),
            CreateClosedPosition(
                id: 3,
                pnl: 6m,
                closedAt: DateTime.UtcNow.AddMinutes(-3),
                exitPrice: 619.20m,
                exitReason: PositionExitReason.StopLoss,
                isOpen: false),
            CreateClosedPosition(
                id: 4,
                pnl: 100m,
                closedAt: DateTime.UtcNow.AddMinutes(-2),
                exitPrice: 620m,
                exitReason: PositionExitReason.TakeProfit,
                isOpen: true),
            CreateClosedPosition(
                id: 5,
                pnl: -3m,
                closedAt: DateTime.UtcNow.AddMinutes(-1),
                exitPrice: null,
                exitReason: PositionExitReason.Time,
                isOpen: false)
        };

        var repository = new FakePositionRepository(positions);
        var service = new TradeAnalyticsService(repository, NullLogger<TradeAnalyticsService>.Instance);

        var summary = await service.GetSummary(CancellationToken.None);

        Assert.Equal(3, summary.TotalTrades);
        Assert.Equal(12m, summary.TotalPnl);
        Assert.Equal(8m, summary.AverageWin);
        Assert.Equal(4m, summary.AverageLoss);
        Assert.Equal(4m, summary.MaxDrawdown);
        Assert.InRange(summary.WinRate, 66.66m, 66.67m);
    }

    private static Position CreateClosedPosition(
        long id,
        decimal pnl,
        DateTime? closedAt,
        decimal? exitPrice,
        PositionExitReason? exitReason,
        bool isOpen)
    {
        return new Position
        {
            Id = id,
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.BUY,
            Quantity = isOpen ? 0.01m : 0m,
            AveragePrice = 617m,
            ExitPrice = exitPrice,
            ExitReason = exitReason,
            OpenedAt = DateTime.UtcNow.AddHours(-1),
            ClosedAt = closedAt,
            RealizedPnl = pnl,
            UnrealizedPnl = 0m,
            IsOpen = isOpen,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
    }

    private sealed class FakePositionRepository(IReadOnlyList<Position> positions) : IPositionRepository
    {
        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default)
            => Task.FromResult(position.Id == 0 ? 1L : position.Id);

        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(positions.FirstOrDefault(x => x.Id == id));

        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(positions.FirstOrDefault(x => x.Symbol == symbol && x.IsOpen));

        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Position>>(positions.Where(x => x.IsOpen).ToList());

        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(positions);

        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
