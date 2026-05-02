using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services;

public class BinanceRateLimiter(IConfiguration configuration, ILogger<BinanceRateLimiter> logger) : IBinanceRateLimiter
{
    private readonly object _lock = new();
    private readonly SlidingWindow _requestWeightWindow = BuildWindow(configuration, "RateLimiterSettings:REQUEST_WEIGHT", fallbackLimit: configuration.GetValue<int?>("BinanceRateLimit:MaxRequestsPerSecond") ?? 10, fallbackIntervalSeconds: 1);
    private readonly SlidingWindow _ordersWindow = BuildWindow(configuration, "RateLimiterSettings:ORDERS", fallbackLimit: 10, fallbackIntervalSeconds: 1);
    private readonly SlidingWindow _rawRequestsWindow = BuildWindow(configuration, "RateLimiterSettings:RAW_REQUESTS", fallbackLimit: 1200, fallbackIntervalSeconds: 60);

    private readonly Queue<DateTime> _requestWeightEvents = new();
    private readonly Queue<DateTime> _orderEvents = new();
    private readonly Queue<DateTime> _rawRequestEvents = new();

    public async Task WaitAsync(
        int requestWeight = 1,
        bool isOrderRequest = false,
        bool isRawRequest = true,
        CancellationToken cancellationToken = default)
    {
        requestWeight = Math.Max(1, requestWeight);

        while (true)
        {
            TimeSpan delay;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                delay = CalculateRequiredDelay(now, requestWeight, isOrderRequest, isRawRequest);
                if (delay <= TimeSpan.Zero)
                {
                    Reserve(now, requestWeight, isOrderRequest, isRawRequest);
                    return;
                }
            }

            logger.LogWarning(
                "Binance rate limiter throttling. RequestWeight={RequestWeight}, IsOrderRequest={IsOrderRequest}, IsRawRequest={IsRawRequest}, DelayMs={DelayMs}",
                requestWeight,
                isOrderRequest,
                isRawRequest,
                Math.Round(delay.TotalMilliseconds, 2));

            await Task.Delay(delay, cancellationToken);
        }
    }

    private TimeSpan CalculateRequiredDelay(DateTime now, int requestWeight, bool isOrderRequest, bool isRawRequest)
    {
        CleanupExpired(_requestWeightEvents, _requestWeightWindow, now);
        CleanupExpired(_orderEvents, _ordersWindow, now);
        CleanupExpired(_rawRequestEvents, _rawRequestsWindow, now);

        var delays = new List<TimeSpan>();

        var requestWeightDelay = GetWindowDelay(_requestWeightEvents, _requestWeightWindow, requestWeight, now);
        if (requestWeightDelay > TimeSpan.Zero)
            delays.Add(requestWeightDelay);

        if (isOrderRequest)
        {
            var orderDelay = GetWindowDelay(_orderEvents, _ordersWindow, 1, now);
            if (orderDelay > TimeSpan.Zero)
                delays.Add(orderDelay);
        }

        if (isRawRequest)
        {
            var rawDelay = GetWindowDelay(_rawRequestEvents, _rawRequestsWindow, 1, now);
            if (rawDelay > TimeSpan.Zero)
                delays.Add(rawDelay);
        }

        return delays.Count == 0 ? TimeSpan.Zero : delays.Max();
    }

    private void Reserve(DateTime now, int requestWeight, bool isOrderRequest, bool isRawRequest)
    {
        for (var i = 0; i < requestWeight; i++)
            _requestWeightEvents.Enqueue(now);

        if (isOrderRequest)
            _orderEvents.Enqueue(now);
        if (isRawRequest)
            _rawRequestEvents.Enqueue(now);
    }

    private static TimeSpan GetWindowDelay(Queue<DateTime> events, SlidingWindow window, int unitsNeeded, DateTime now)
    {
        if (events.Count + unitsNeeded <= window.Limit)
            return TimeSpan.Zero;

        var overflow = (events.Count + unitsNeeded) - window.Limit;
        if (overflow <= 0)
            return TimeSpan.Zero;

        var array = events.ToArray();
        var oldestBlocking = array[Math.Min(overflow - 1, array.Length - 1)];
        var availableAt = oldestBlocking + window.Window;
        return availableAt > now ? availableAt - now : TimeSpan.Zero;
    }

    private static void CleanupExpired(Queue<DateTime> events, SlidingWindow window, DateTime now)
    {
        while (events.Count > 0)
        {
            var head = events.Peek();
            if ((now - head) >= window.Window)
            {
                events.Dequeue();
                continue;
            }
            break;
        }
    }

    private static SlidingWindow BuildWindow(IConfiguration configuration, string sectionPath, int fallbackLimit, int fallbackIntervalSeconds)
    {
        var limit = Math.Max(1, configuration.GetValue<int?>($"{sectionPath}:limit") ?? fallbackLimit);
        var intervalNum = Math.Max(1, configuration.GetValue<int?>($"{sectionPath}:intervalNum") ?? 1);
        var interval = configuration.GetValue<string>($"{sectionPath}:interval");
        var secondsPerUnit = interval?.ToUpperInvariant() switch
        {
            "SECOND" => 1,
            "MINUTE" => 60,
            "HOUR" => 3600,
            "DAY" => 86400,
            _ => fallbackIntervalSeconds
        };

        return new SlidingWindow(limit, TimeSpan.FromSeconds(intervalNum * secondsPerUnit));
    }

    private sealed record SlidingWindow(int Limit, TimeSpan Window);
}
