using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services;

public class BinanceRateLimiter(IConfiguration configuration, ILogger<BinanceRateLimiter> logger) : IBinanceRateLimiter
{
    private readonly object _lock = new();
    private DateTime _nextAllowedUtc = DateTime.UtcNow;
    private int _maxRequestsPerSecond = configuration.GetValue<int?>("BinanceRateLimit:MaxRequestsPerSecond") ?? 10;

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        TimeSpan delay;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (now >= _nextAllowedUtc)
                _nextAllowedUtc = now;
            delay = _nextAllowedUtc - now;
            _nextAllowedUtc += TimeSpan.FromSeconds(1.0 / _maxRequestsPerSecond);
        }

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
    }
}
