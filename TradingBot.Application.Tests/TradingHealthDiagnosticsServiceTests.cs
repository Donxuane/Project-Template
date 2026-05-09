using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.Services;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Diagnostics;
using Xunit;

namespace TradingBot.Application.Tests;

public class TradingHealthDiagnosticsServiceTests
{
    [Fact]
    public async Task RunAsync_ReturnsHealthy_WhenNoIssues()
    {
        var repository = new FakeTradingHealthDiagnosticsRepository
        {
            Metrics = CreateHealthyMetrics()
        };
        var service = new TradingHealthDiagnosticsService(repository, NullLogger<TradingHealthDiagnosticsService>.Instance);

        var result = await service.RunAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.True(result.IsHealthy);
        Assert.Empty(result.CriticalIssues);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RunAsync_ReturnsUnhealthy_WhenSchemaMissing()
    {
        var metrics = CreateHealthyMetrics();
        metrics.HasPositionsIsClosingColumn = false;

        var service = new TradingHealthDiagnosticsService(
            new FakeTradingHealthDiagnosticsRepository { Metrics = metrics },
            NullLogger<TradingHealthDiagnosticsService>.Instance);

        var result = await service.RunAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Contains(result.CriticalIssues, x => x.Contains("positions.is_closing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_ReturnsUnhealthy_WhenClosedPositionHasNonZeroQuantity()
    {
        var metrics = CreateHealthyMetrics();
        metrics.ClosedPositionsWithNonZeroQuantity = 2;

        var service = new TradingHealthDiagnosticsService(
            new FakeTradingHealthDiagnosticsRepository { Metrics = metrics },
            NullLogger<TradingHealthDiagnosticsService>.Instance);

        var result = await service.RunAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Contains(result.CriticalIssues, x => x.Contains("quantity <> 0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_AddsWarning_WhenBalanceIsStale()
    {
        var metrics = CreateHealthyMetrics();
        metrics.LatestBnbAt = DateTime.UtcNow.AddMinutes(-30);

        var service = new TradingHealthDiagnosticsService(
            new FakeTradingHealthDiagnosticsRepository { Metrics = metrics },
            NullLogger<TradingHealthDiagnosticsService>.Instance);

        var result = await service.RunAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.True(result.IsHealthy);
        Assert.Contains(result.Warnings, x => x.Contains("BNB balance snapshot is stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_ReturnsCritical_WhenDuplicateActiveCloseOrdersExist()
    {
        var metrics = CreateHealthyMetrics();
        metrics.ParentPositionsWithMultipleActiveCloseOrders = 1;

        var service = new TradingHealthDiagnosticsService(
            new FakeTradingHealthDiagnosticsRepository { Metrics = metrics },
            NullLogger<TradingHealthDiagnosticsService>.Instance);

        var result = await service.RunAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Contains(result.CriticalIssues, x => x.Contains("More than one active close order", StringComparison.OrdinalIgnoreCase));
    }

    private static TradingRuntimeHealthMetrics CreateHealthyMetrics()
    {
        return new TradingRuntimeHealthMetrics
        {
            HasPositionsIsClosingColumn = true,
            HasBalanceSnapshotHistoryTable = true,
            LatestBnbAt = DateTime.UtcNow,
            LatestUsdtAt = DateTime.UtcNow,
            BalanceSnapshotHistoryEmpty = false
        };
    }

    private sealed class FakeTradingHealthDiagnosticsRepository : ITradingHealthDiagnosticsRepository
    {
        public TradingRuntimeHealthMetrics Metrics { get; set; } = new();

        public Task<TradingRuntimeHealthMetrics> CollectMetricsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Metrics);
    }
}
