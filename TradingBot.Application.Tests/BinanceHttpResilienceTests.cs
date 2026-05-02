using System.Net;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.MarketData;
using TradingBot.Percistance.Services.Main;
using TradingBot.Shared.Shared.Models;
using Xunit;

namespace TradingBot.Application.Tests;

public class BinanceHttpResilienceTests
{
    [Fact]
    public async Task TransientHttpFailure_IsRetried()
    {
        var handler = new SequenceHandler(
            _ => throw new HttpRequestException("connection reset"),
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":"ok"}""")
            });
        var service = BuildClient(handler, out _, out _);

        var response = await service.Call<SimpleResponse, object?>(
            null,
            new Endpoint { API = "/api/v3/ping", Type = "GET" },
            enableSignature: false);

        Assert.Equal("ok", response.Value);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ValidationError_IsNotRetried()
    {
        var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"code":-1013,"msg":"Filter failure: LOT_SIZE"}""")
            });
        var service = BuildClient(handler, out _, out _);

        var ex = await Assert.ThrowsAsync<Exception>(() => service.Call<SimpleResponse, object?>(
            null,
            new Endpoint { API = "/api/v3/order", Type = "POST" },
            enableSignature: true));

        Assert.Contains("LOT_SIZE", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Http429_UsesRateLimitRetryBehavior()
    {
        var handler = new SequenceHandler(
            _ =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("""{"code":-1003,"msg":"Too many requests"}""")
                };
                response.Headers.TryAddWithoutValidation("Retry-After", "1");
                return response;
            },
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":"ok"}""")
            });
        var service = BuildClient(handler, out _, out _);
        var started = DateTime.UtcNow;

        var result = await service.Call<SimpleResponse, object?>(
            null,
            new Endpoint { API = "/api/v3/ping", Type = "GET" },
            enableSignature: false);

        var elapsed = DateTime.UtcNow - started;
        Assert.Equal("ok", result.Value);
        Assert.Equal(2, handler.CallCount);
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task TimestampError_RefreshesOffset_AndRetriesOnce()
    {
        var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"code":-1021,"msg":"Timestamp for this request is outside of the recvWindow."}""")
            },
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":"ok"}""")
            });
        var service = BuildClient(handler, out var timeSync, out _);

        var response = await service.Call<SimpleResponse, AccountInfoRequest>(
            new AccountInfoRequest { Timestamp = 1, RecvWindow = 30000 },
            new Endpoint { API = "/api/v3/account", Type = "GET" },
            enableSignature: true);

        Assert.Equal("ok", response.Value);
        Assert.Equal(1, timeSync.RefreshCalls);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RateLimiter_Throttles_WhenBudgetExceeded()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiterSettings:REQUEST_WEIGHT:interval"] = "SECOND",
                ["RateLimiterSettings:REQUEST_WEIGHT:intervalNum"] = "1",
                ["RateLimiterSettings:REQUEST_WEIGHT:limit"] = "1",
                ["RateLimiterSettings:ORDERS:interval"] = "SECOND",
                ["RateLimiterSettings:ORDERS:intervalNum"] = "1",
                ["RateLimiterSettings:ORDERS:limit"] = "100",
                ["RateLimiterSettings:RAW_REQUESTS:interval"] = "SECOND",
                ["RateLimiterSettings:RAW_REQUESTS:intervalNum"] = "1",
                ["RateLimiterSettings:RAW_REQUESTS:limit"] = "100"
            })
            .Build();

        var limiter = new TradingBot.Percistance.Services.BinanceRateLimiter(config, NullLogger<TradingBot.Percistance.Services.BinanceRateLimiter>.Instance);
        var start = DateTime.UtcNow;
        await limiter.WaitAsync();
        await limiter.WaitAsync();
        var elapsed = DateTime.UtcNow - start;

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task MarketDataWorker_PerSymbolFallback_HandlesPartialFailure()
    {
        var worker = new MarketDataWorker(
            new NoopScopeFactory(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:MaxConcurrency"] = "2"
            }).Build(),
            NullLogger<MarketDataWorker>.Instance);

        var toolService = new FakeToolService(new SymbolFailingBinanceClientService("BTCUSDT"));
        var priceCache = new FakePriceCacheService();
        var endpoint = new Endpoint { API = "/api/v3/ticker/price", Type = "GET" };
        var symbols = new[] { TradingSymbol.BTCUSDT, TradingSymbol.BNBUSDT, TradingSymbol.ETHUSDT };

        var method = typeof(MarketDataWorker).GetMethod("FetchPerSymbolAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(worker, new object[] { toolService, priceCache, endpoint, symbols, 2, CancellationToken.None });
        Assert.NotNull(task);
        await task!;

        Assert.Equal(2, priceCache.Stored.Count);
        Assert.Contains(TradingSymbol.BNBUSDT, priceCache.Stored.Keys);
        Assert.Contains(TradingSymbol.ETHUSDT, priceCache.Stored.Keys);
    }

    private static BinanceClientService BuildClient(SequenceHandler handler, out FakeTimeSyncService timeSyncService, out FakeBinanceRateLimiter rateLimiter)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseURL"] = "https://testnet.binance.vision",
                ["ApiKey"] = "test-api-key",
                ["SecretKey"] = "test-secret-key",
                ["Binance:Http:TimeoutSeconds"] = "15",
                ["Binance:Http:RetryCount"] = "3",
                ["Binance:Http:RetryBaseDelayMilliseconds"] = "10"
            })
            .Build();

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://testnet.binance.vision")
        };

        timeSyncService = new FakeTimeSyncService();
        rateLimiter = new FakeBinanceRateLimiter();
        return new BinanceClientService(
            configuration,
            httpClient,
            timeSyncService,
            rateLimiter,
            NullLogger<BinanceClientService>.Instance);
    }

    private sealed class SimpleResponse
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _steps = new(steps);
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_steps.Count == 0)
                throw new InvalidOperationException("No response configured for this request.");

            var step = _steps.Dequeue();
            return Task.FromResult(step(request));
        }
    }

    private sealed class FakeTimeSyncService : ITimeSyncService
    {
        public int RefreshCalls { get; private set; }
        public Task<long> GetAdjustedTimestampAsync(CancellationToken cancellationToken = default) => Task.FromResult(1735689600000L);
        public Task<long> RefreshOffsetAsync(CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.FromResult(1735689600000L);
        }
    }

    private sealed class FakeBinanceRateLimiter : IBinanceRateLimiter
    {
        public int Calls { get; private set; }

        public Task WaitAsync(int requestWeight = 1, bool isOrderRequest = false, bool isRawRequest = true, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }

    private sealed class SymbolFailingBinanceClientService(string failingSymbol) : IBinanceClientService
    {
        public Task<TResponse> Call<TResponse, TRequest>(TRequest? request, Endpoint endpoint, bool enableSignature)
        {
            if (request is SymbolPriceTickerRequest tickerRequest && tickerRequest.Symbol == failingSymbol)
                throw new HttpRequestException("forced symbol failure");

            object response = typeof(TResponse) == typeof(SymbolPriceTickerResponse)
                ? new SymbolPriceTickerResponse
                {
                    Symbol = (request as SymbolPriceTickerRequest)?.Symbol ?? TradingSymbol.BNBUSDT.ToString(),
                    Price = "631.12"
                }
                : throw new NotSupportedException("Unsupported response type");

            return Task.FromResult((TResponse)response);
        }
    }

    private sealed class FakeToolService(IBinanceClientService binanceClientService) : IToolService
    {
        public IBinanceClientService BinanceClientService { get; } = binanceClientService;
        public IBinanceEndpointsService BinanceEndpointsService { get; } = new FakeBinanceEndpointsService();
        public IBinanceSettingsService BinanceSettingsService => throw new NotSupportedException();
        public IRedisCacheService RedisCacheService => throw new NotSupportedException();
        public IOrderValidator OrderValidator => throw new NotSupportedException();
        public ISlicerService SlicerService => throw new NotSupportedException();
        public IAICLinetService AICLinetService => throw new NotSupportedException();
    }

    private sealed class FakeBinanceEndpointsService : IBinanceEndpointsService
    {
        public Endpoint GetEndpoint(Domain.Enums.Endpoints.Account account) => throw new NotSupportedException();
        public Endpoint GetEndpoint(Domain.Enums.Endpoints.GeneralApis general) => throw new NotSupportedException();
        public Endpoint GetEndpoint(Domain.Enums.Endpoints.MarketData marketData) => new() { API = "/api/v3/ticker/price", Type = "GET" };
        public Endpoint GetEndpoint(Domain.Enums.Endpoints.Trading trading) => throw new NotSupportedException();
    }

    private sealed class FakePriceCacheService : IPriceCacheService
    {
        public Dictionary<TradingSymbol, decimal> Stored { get; } = new();

        public Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(Stored.TryGetValue(symbol, out var value) ? (decimal?)value : null);

        public Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default)
        {
            Stored[symbol] = price;
            return Task.CompletedTask;
        }
    }
}
