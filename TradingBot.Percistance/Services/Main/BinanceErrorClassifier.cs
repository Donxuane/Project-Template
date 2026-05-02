using System.Net;

namespace TradingBot.Percistance.Services.Main;

public enum BinanceErrorCategory
{
    TransientNetwork = 0,
    RateLimited = 1,
    TimestampOrSignature = 2,
    ValidationError = 3,
    InsufficientBalance = 4,
    PrecisionOrFilterError = 5,
    Unknown = 6
}

public sealed record BinanceErrorClassification(
    BinanceErrorCategory Category,
    bool IsRetryable,
    bool ShouldRefreshTimeOffset,
    string Reason);

public static class BinanceErrorClassifier
{
    public static BinanceErrorClassification Classify(
        HttpStatusCode? statusCode,
        int? binanceCode,
        string? binanceMessage,
        Exception? exception = null)
    {
        var message = binanceMessage?.ToLowerInvariant() ?? string.Empty;

        if (statusCode is HttpStatusCode.TooManyRequests || (int?)statusCode == 418)
        {
            return new BinanceErrorClassification(
                BinanceErrorCategory.RateLimited,
                true,
                false,
                "HTTP rate limited response.");
        }

        if (statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout)
        {
            return new BinanceErrorClassification(
                BinanceErrorCategory.TransientNetwork,
                true,
                false,
                "HTTP transient server/timeout response.");
        }

        if (exception is HttpRequestException or IOException)
        {
            return new BinanceErrorClassification(
                BinanceErrorCategory.TransientNetwork,
                true,
                false,
                "Transient transport exception.");
        }

        if (exception is TaskCanceledException)
        {
            return new BinanceErrorClassification(
                BinanceErrorCategory.TransientNetwork,
                true,
                false,
                "HTTP request timed out.");
        }

        if (binanceCode is -1021 or -1022
            || message.Contains("timestamp")
            || message.Contains("signature"))
        {
            return new BinanceErrorClassification(
                BinanceErrorCategory.TimestampOrSignature,
                binanceCode == -1021 || message.Contains("timestamp"),
                binanceCode == -1021 || message.Contains("timestamp"),
                "Timestamp/signature related Binance error.");
        }

        if (binanceCode == -2010 || message.Contains("insufficient balance"))
        {
            return new BinanceErrorClassification(
                BinanceErrorCategory.InsufficientBalance,
                false,
                false,
                "Insufficient balance Binance error.");
        }

        if (message.Contains("lot_size")
            || message.Contains("price_filter")
            || message.Contains("min_notional")
            || message.Contains("precision")
            || message.Contains("too much precision")
            || message.Contains("filter failure"))
        {
            return new BinanceErrorClassification(
                BinanceErrorCategory.PrecisionOrFilterError,
                false,
                false,
                "Filter/precision Binance validation error.");
        }

        if ((int?)statusCode is >= 400 and < 500)
        {
            return new BinanceErrorClassification(
                BinanceErrorCategory.ValidationError,
                false,
                false,
                "Client-side Binance validation/auth error.");
        }

        return new BinanceErrorClassification(
            BinanceErrorCategory.Unknown,
            false,
            false,
            "Unknown Binance error type.");
    }
}
