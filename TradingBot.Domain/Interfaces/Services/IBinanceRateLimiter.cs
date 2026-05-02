namespace TradingBot.Domain.Interfaces.Services;

/// <summary>
/// Limits the rate of Binance API requests (e.g. max requests per second).
/// </summary>
public interface IBinanceRateLimiter
{
    /// <summary>Waits until a request slot is available, then returns. Call before each Binance request.</summary>
    Task WaitAsync(
        int requestWeight = 1,
        bool isOrderRequest = false,
        bool isRawRequest = true,
        CancellationToken cancellationToken = default);
}
