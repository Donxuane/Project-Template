using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class FuturesMarketDataExpansionV1Tests
{
    [Fact]
    public void Catalog_ClassifiesHistoryAvailabilityHonestly()
    {
        var sources = FuturesMarketDataCatalog.Sources();
        Assert.Contains(sources, s => s.SourceKey == "funding" && s.Availability == FuturesDataAvailabilityClass.FullHistory);
        Assert.Contains(sources, s => s.SourceKey == "openInterestHist" && s.Availability == FuturesDataAvailabilityClass.Limited30d);
        Assert.Contains(sources, s => s.SourceKey == "liquidations" && s.Availability == FuturesDataAvailabilityClass.NotPublicFree);
        Assert.True(FuturesMarketDataCatalog.Is365dCapable("funding"));
        Assert.False(FuturesMarketDataCatalog.Is365dCapable("openInterestHist"));
    }

    [Fact]
    public void QualityAnalyzer_FlagsCadenceAndAlignment()
    {
        var start = new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var cadence = 30L * 60_000L;
        var timestamps = Enumerable.Range(0, 200).Select(i => start + i * cadence).ToArray();
        var samples = timestamps.Take(100).ToList();

        var row = FuturesMarketDataQualityAnalyzer.Analyze(
            TradingSymbol.BTCUSDT, "openInterestHist", timestamps, "oi:100%",
            DateTimeOffset.FromUnixTimeMilliseconds(timestamps[0]).UtcDateTime,
            DateTimeOffset.FromUnixTimeMilliseconds(timestamps[^1]).UtcDateTime,
            samples);

        Assert.Equal(200, row.RecordCount);
        Assert.Equal(0, row.GapCount);
        Assert.Equal(0, row.DuplicateTimestampCount);
        Assert.True(row.AlignedWithCandles);
        Assert.Equal("Clean", row.Verdict);
    }

    [Fact]
    public void FlowFeatureBuilder_ComputesAsOfFundingAndZScore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "futures-flow-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FuturesMarketDataCatalog.FuturesDataDirectory(dir));
        var path = FuturesMarketDataCatalog.FilePath(dir, TradingSymbol.BTCUSDT, "funding");
        var baseTime = new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var rows = Enumerable.Range(0, 40)
            .Select(i => $"{{\"t\":{baseTime + i * 8L * 3600_000L},\"rate\":\"{(0.0001m * ((i % 5) - 2)).ToString(System.Globalization.CultureInfo.InvariantCulture)}\"}}");
        File.WriteAllText(path, "[" + string.Join(",", rows) + "]");

        try
        {
            var loader = new FuturesMarketDataLoader(dir);
            var builder = new FuturesFlowFeatureBuilder(loader, TradingSymbol.BTCUSDT);
            Assert.True(builder.HasFunding);

            var entry = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc);
            var features = builder.Build(entry);
            Assert.NotNull(features.FundingRate);
            Assert.NotNull(features.FundingRateZScore);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Loader_ReturnsEmptyWhenMissing()
    {
        var loader = new FuturesMarketDataLoader(Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N")));
        Assert.False(loader.Exists(TradingSymbol.ETHUSDT, "funding"));
        Assert.Empty(loader.LoadFunding(TradingSymbol.ETHUSDT));
    }
}
