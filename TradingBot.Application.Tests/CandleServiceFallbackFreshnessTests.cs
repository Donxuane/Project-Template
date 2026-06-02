using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.DecisionEngine;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models.MarketData;
using TradingBot.Shared.Shared.Models;
using Xunit;

namespace TradingBot.Application.Tests;

public class CandleServiceFallbackFreshnessTests
{
    [Fact]
    public async Task KlineFallback_UsesLatestClosedCandleTimestampForAsOfAndAge()
    {
        var nowUtc = DateTime.UtcNow;
        var completedOpen = nowUtc.AddMinutes(-2);
        var completedClose = nowUtc.AddMinutes(-1);
        var inProgressOpen = nowUtc.AddMinutes(-1);
        var inProgressClose = nowUtc.AddMinutes(1);

        var klinePayload = BuildKlinePayload(
            (completedOpen, completedClose, 100.5m, 99.5m, 100.0m, 10m),
            (inProgressOpen, inProgressClose, 101.0m, 100.0m, 100.8m, 12m));

        var services = new ServiceCollection();
        services.AddScoped<IToolService>(_ => new FakeToolService(klinePayload));
        services.AddScoped<IPriceCacheService>(_ => new NullPriceCacheService());
        using var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DecisionEngine:Candles:Interval"] = "1m",
                ["DecisionEngine:Candles:RefreshLimit"] = "2",
                ["DecisionEngine:Candles:BackfillPadding"] = "0",
                ["DecisionEngine:Candles:MaxBufferSize"] = "20"
            })
            .Build();

        var candleService = new CandleService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<CandleService>.Instance);

        var snapshot = await candleService.GetSnapshotAsync(TradingSymbol.ETHUSDT, requiredCandles: 2, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("KlineFallback", snapshot!.CurrentPriceSource);
        var expectedCompletedOpen = new DateTime(completedOpen.Ticks - (completedOpen.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
        var expectedCompletedClose = new DateTime(completedClose.Ticks - (completedClose.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
        Assert.Equal(expectedCompletedClose, snapshot.CurrentPriceAsOfUtc);
        Assert.Equal(expectedCompletedOpen, snapshot.LatestClosedCandleOpenTimeUtc);
        Assert.Equal(expectedCompletedClose, snapshot.LatestClosedCandleCloseTimeUtc);
        var expectedAgeSeconds = (decimal)(DateTime.UtcNow - expectedCompletedClose).TotalSeconds;
        Assert.True(snapshot.LatestClosedCandleAgeSeconds >= 0m);
        Assert.InRange(snapshot.LatestClosedCandleAgeSeconds!.Value, expectedAgeSeconds - 3m, expectedAgeSeconds + 3m);
        Assert.Equal(100.0m, snapshot.LatestClosedCandleClosePrice);
        Assert.Equal(100.0m, snapshot.CurrentPrice);
        Assert.True(snapshot.MarketDataAgeSeconds >= 0m);
        Assert.InRange(snapshot.MarketDataAgeSeconds!.Value, expectedAgeSeconds - 3m, expectedAgeSeconds + 3m);
    }

    private static JsonElement BuildKlinePayload(params (DateTime open, DateTime close, decimal high, decimal low, decimal closePrice, decimal volume)[] candles)
    {
        var rows = candles.Select(c => new object[]
        {
            new DateTimeOffset(c.open).ToUnixTimeMilliseconds(),
            "0",
            c.high.ToString(System.Globalization.CultureInfo.InvariantCulture),
            c.low.ToString(System.Globalization.CultureInfo.InvariantCulture),
            c.closePrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
            c.volume.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new DateTimeOffset(c.close).ToUnixTimeMilliseconds()
        }).ToArray();

        var json = JsonSerializer.Serialize(rows);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private sealed class NullPriceCacheService : IPriceCacheService
    {
        public Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<decimal?>(null);

        public Task<PriceSnapshot?> GetCachedPriceSnapshotAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<PriceSnapshot?>(null);

        public Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeToolService(JsonElement klinePayload) : IToolService
    {
        public IBinanceClientService BinanceClientService { get; } = new FakeBinanceClientService(klinePayload);
        public IBinanceEndpointsService BinanceEndpointsService { get; } = new FakeBinanceEndpointsService();
        public IBinanceSettingsService BinanceSettingsService => throw new NotSupportedException();
        public IRedisCacheService RedisCacheService => throw new NotSupportedException();
        public IOrderValidator OrderValidator => throw new NotSupportedException();
        public ISlicerService SlicerService => throw new NotSupportedException();
        public IAICLinetService AICLinetService => throw new NotSupportedException();
    }

    private sealed class FakeBinanceClientService(JsonElement klinePayload) : IBinanceClientService
    {
        public Task<TResponse> Call<TResponse, TRequest>(TRequest? request, Endpoint endpoint, bool enableSignature)
        {
            if (typeof(TResponse) != typeof(JsonElement))
                throw new NotSupportedException("Only JsonElement responses are supported in this test.");

            object response = klinePayload.Clone();
            return Task.FromResult((TResponse)response);
        }
    }

    private sealed class FakeBinanceEndpointsService : IBinanceEndpointsService
    {
        public Endpoint GetEndpoint(Account account) => throw new NotSupportedException();
        public Endpoint GetEndpoint(GeneralApis general) => throw new NotSupportedException();
        public Endpoint GetEndpoint(TradingBot.Domain.Enums.Endpoints.MarketData marketData) => new() { API = "/api/v3/klines", Type = "GET" };
        public Endpoint GetEndpoint(TradingBot.Domain.Enums.Endpoints.Trading trading) => throw new NotSupportedException();
    }
}
