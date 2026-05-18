using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;
using Xunit;

namespace TradingBot.Application.Tests;

public class MarketSnapshotMetadataTests
{
    [Fact]
    public void MarketSnapshot_ExposesPriceSource_AsOf_AndAge()
    {
        var asOfUtc = DateTime.UtcNow.AddSeconds(-4);
        var latestClosedOpen = asOfUtc.AddMinutes(-1);
        var latestClosedClose = asOfUtc;
        var snapshot = new MarketSnapshot
        {
            Symbol = TradingSymbol.ETHUSDT,
            CurrentPrice = 2500m,
            CurrentPriceSource = "RedisTicker",
            CurrentPriceAsOfUtc = asOfUtc,
            MarketDataAgeSeconds = 4m,
            LatestClosedCandleOpenTimeUtc = latestClosedOpen,
            LatestClosedCandleCloseTimeUtc = latestClosedClose,
            LatestClosedCandleClosePrice = 2499m,
            ClosePrices = [2490m, 2495m, 2500m]
        };

        Assert.Equal("RedisTicker", snapshot.CurrentPriceSource);
        Assert.Equal(asOfUtc, snapshot.CurrentPriceAsOfUtc);
        Assert.Equal(4m, snapshot.MarketDataAgeSeconds);
        Assert.Equal(latestClosedOpen, snapshot.LatestClosedCandleOpenTimeUtc);
        Assert.Equal(latestClosedClose, snapshot.LatestClosedCandleCloseTimeUtc);
        Assert.Equal(2499m, snapshot.LatestClosedCandleClosePrice);
    }
}
