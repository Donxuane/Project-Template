using Microsoft.Extensions.Configuration;
using TradingBot.Application.SpotFuturesCrossMarket;
using TradingBot.Domain.Enums.Binance;
using Xunit;

namespace TradingBot.Application.Tests;

public class AdaptiveRollingProfitExitV1Tests
{
    [Fact]
    public void ProjectedNetExitPnl_Long_UsesExecutablePriceFeesFundingAndReserve()
    {
        var result = AdaptiveRollingProfitExitCalculator.Calculate(
            OrderSide.BUY,
            averageEntryPrice: 100m,
            estimatedExecutablePrice: 101m,
            remainingQuantity: 2m,
            actualAllocatedEntryCommissions: 0.10m,
            estimatedTakerCommissionRate: 0.0005m,
            signedFunding: 0.03m,
            adverseMoveReserve: 0.02m);

        Assert.Equal(2.00m, result.GrossPnl);
        Assert.Equal(0.1010m, result.EstimatedExitCommission);
        Assert.Equal(1.8090m, result.ProjectedNetPnl);
        Assert.True(result.BreakEvenExecutablePrice > 100m);
    }

    [Fact]
    public void ProjectedNetExitPnl_Short_IsSymmetric()
    {
        var result = AdaptiveRollingProfitExitCalculator.Calculate(
            OrderSide.SELL,
            averageEntryPrice: 100m,
            estimatedExecutablePrice: 99m,
            remainingQuantity: 2m,
            actualAllocatedEntryCommissions: 0.10m,
            estimatedTakerCommissionRate: 0.0005m,
            signedFunding: 0m,
            adverseMoveReserve: 0.02m);

        Assert.Equal(2.00m, result.GrossPnl);
        Assert.Equal(0.0990m, result.EstimatedExitCommission);
        Assert.Equal(1.7810m, result.ProjectedNetPnl);
        Assert.True(result.BreakEvenExecutablePrice < 100m);
    }

    [Fact]
    public void Settings_Defaults_PreventSinglePositiveTickArming()
    {
        var settings = AdaptiveRollingProfitExitV1Settings.Load(new ConfigurationBuilder().Build());

        Assert.True(settings.EligibilityConsecutiveObservations > 1);
        Assert.True(settings.EligibilityDwellMs > 0);
        Assert.True(settings.EntryProfitArmThreshold(1000m) > 0m);
        Assert.True(settings.CloseProfitFloor(1000m) > 0m);
    }

    [Fact]
    public void Settings_NeverAllowZeroFeeFallback()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdaptiveRollingProfitExitV1:ConservativeFallbackTakerCommissionRate"] = "0",
                ["AdaptiveRollingProfitExitV1:ConservativeFallbackMakerCommissionRate"] = "0"
            })
            .Build();

        var settings = AdaptiveRollingProfitExitV1Settings.Load(config);

        Assert.True(settings.ConservativeFallbackTakerCommissionRate > 0m);
        Assert.True(settings.ConservativeFallbackMakerCommissionRate > 0m);
    }
}
