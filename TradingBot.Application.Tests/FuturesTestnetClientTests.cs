using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Percistance.Services.Main;
using Xunit;

namespace TradingBot.Application.Tests;

public sealed class FuturesTestnetClientTests
{
    [Fact]
    public async Task LeverageUpdate_RetriesTransientBackendTimeout()
    {
        var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.RequestTimeout)
            {
                Content = new StringContent("""{"code":-1007,"msg":"Timeout waiting for response from backend server."}""")
            },
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"symbol":"BTCUSDT","leverage":3}""")
            });
        var client = CreateClient(handler);

        await client.EnsureLeverageAsync("BTCUSDT", 3);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task MarketOrder_UsesImmediateAcknowledgementAndStableClientOrderId()
    {
        string? requestBody = null;
        var handler = new SequenceHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"orderId":123,"symbol":"BTCUSDT","side":"SELL","status":"NEW","executedQty":"0","avgPrice":"0","cumQuote":"0","updateTime":1}""")
            };
        });
        var client = CreateClient(handler);

        var result = await client.PlaceMarketOrderAsync(
            "BTCUSDT",
            OrderSide.SELL,
            0.001m,
            reduceOnly: false,
            clientOrderId: "spot-feature-order-1");

        Assert.Equal(123, result.OrderId);
        Assert.Contains("newOrderRespType=ACK", requestBody);
        Assert.Contains("newClientOrderId=spot-feature-order-1", requestBody);
    }

    [Fact]
    public async Task MainnetHost_IsRejectedBeforeRequest()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler, "https://fapi.binance.com");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetMarkPriceAsync("BTCUSDT"));

        Assert.Contains("refuses to call mainnet", error.Message);
        Assert.Equal(0, handler.CallCount);
    }

    private static FuturesTestnetClient CreateClient(
        HttpMessageHandler handler,
        string baseUrl = "https://demo-fapi.binance.com")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FuturesTestnet:ApiKey"] = "test-api-key",
                ["FuturesTestnet:SecretKey"] = "test-secret-key"
            })
            .Build();
        return new FuturesTestnetClient(
            new HttpClient(handler) { BaseAddress = new Uri(baseUrl) },
            configuration,
            NullLogger<FuturesTestnetClient>.Instance);
    }

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _steps = new(steps);

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (_steps.Count == 0)
                throw new InvalidOperationException("No response configured for this request.");
            return Task.FromResult(_steps.Dequeue()(request));
        }
    }
}
