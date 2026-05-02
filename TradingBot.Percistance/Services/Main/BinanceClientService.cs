using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Utilities;
using TradingBot.Shared.Shared.Models;

namespace TradingBot.Percistance.Services.Main;

public class BinanceClientService : IBinanceClientService
{
    private readonly HttpClient _httpClient;
    private readonly ITimeSyncService _timeSyncService;
    private readonly IBinanceRateLimiter _rateLimiter;
    private readonly ILogger<BinanceClientService> _logger;
    private readonly int _retryCount;
    private readonly int _retryBaseDelayMs;
    private readonly int _requestTimeoutSeconds;
    private readonly string? _apiKey;
    private readonly string? _secretKey;
    private readonly Random _jitter = new();

    public BinanceClientService(
        IConfiguration configuration,
        HttpClient httpClient,
        ITimeSyncService timeSyncService,
        IBinanceRateLimiter rateLimiter,
        ILogger<BinanceClientService> logger)
    {
        _httpClient = httpClient;
        _timeSyncService = timeSyncService;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _apiKey = configuration.GetValue<string>("ApiKey");
        _secretKey = configuration.GetValue<string>("SecretKey");
        _retryCount = Math.Max(0, configuration.GetValue<int?>("Binance:Http:RetryCount") ?? 3);
        _retryBaseDelayMs = Math.Max(50, configuration.GetValue<int?>("Binance:Http:RetryBaseDelayMilliseconds") ?? 300);
        _requestTimeoutSeconds = Math.Max(1, configuration.GetValue<int?>("Binance:Http:TimeoutSeconds") ?? 15);
    }

    public async Task<TResponse> Call<TResponse, TRequest>(TRequest? request, Endpoint endpoint, bool enableSignature)
    {
        if (string.IsNullOrWhiteSpace(endpoint.API))
            throw new ArgumentException("Endpoint API path is missing");
        if (string.IsNullOrWhiteSpace(endpoint.Type))
            throw new ArgumentException("Endpoint method is missing");

        var requestDict = BinanceRequestQueryBuilder.BuildRequestDictionary(request);
        var symbol = TryGetRequestValue(requestDict, "symbol");
        var correlationId = TryGetRequestValue(requestDict, "correlationId")
                            ?? TryGetRequestValue(requestDict, "correlationid");
        var isOrderRequest = endpoint.API.Contains("/order", StringComparison.OrdinalIgnoreCase);
        var requestWeight = 1;
        var timestampRefreshAttempted = false;

        for (var attempt = 1; attempt <= _retryCount + 1; attempt++)
        {
            try
            {
                await _rateLimiter.WaitAsync(
                    requestWeight: requestWeight,
                    isOrderRequest: isOrderRequest,
                    isRawRequest: true);

                if (enableSignature && requestDict.ContainsKey("timestamp"))
                {
                    requestDict["timestamp"] = (await _timeSyncService.GetAdjustedTimestampAsync())
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                var queryString = BinanceRequestQueryBuilder.BuildQueryString(requestDict);
                if (enableSignature)
                {
                    var signature = CreateSignature(queryString);
                    queryString = string.IsNullOrEmpty(queryString)
                        ? $"signature={signature}"
                        : $"{queryString}&signature={signature}";
                }

                using var message = BuildRequestMessage(endpoint, queryString, enableSignature);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(_requestTimeoutSeconds));
                var stopwatch = Stopwatch.StartNew();

                using var response = await _httpClient.SendAsync(message, linkedCts.Token);
                var responseBody = await response.Content.ReadAsStringAsync();
                stopwatch.Stop();

                var (binanceCode, binanceMessage) = TryParseBinanceError(responseBody);
                if (!response.IsSuccessStatusCode)
                {
                    var classification = BinanceErrorClassifier.Classify(
                        response.StatusCode,
                        binanceCode,
                        binanceMessage);
                    var retryAfterDelay = ResolveRetryAfterDelay(response);
                    var statusCode = (int)response.StatusCode;

                    _logger.LogWarning(
                        "Binance call failed. Endpoint={Endpoint}, Method={Method}, Signed={Signed}, StatusCode={StatusCode}, BinanceCode={BinanceCode}, BinanceMessage={BinanceMessage}, Category={Category}, Retryable={Retryable}, Attempt={Attempt}, MaxAttempts={MaxAttempts}, ElapsedMs={ElapsedMs}, CorrelationId={CorrelationId}, Symbol={Symbol}",
                        endpoint.API,
                        endpoint.Type,
                        enableSignature,
                        statusCode,
                        binanceCode,
                        binanceMessage,
                        classification.Category,
                        classification.IsRetryable,
                        attempt,
                        _retryCount + 1,
                        stopwatch.ElapsedMilliseconds,
                        correlationId,
                        symbol);

                    if (classification.ShouldRefreshTimeOffset
                        && !timestampRefreshAttempted
                        && enableSignature)
                    {
                        timestampRefreshAttempted = true;
                        _logger.LogWarning(
                            "Binance timestamp/signature issue detected. Refreshing time offset and retrying once. Endpoint={Endpoint}, Symbol={Symbol}, CorrelationId={CorrelationId}",
                            endpoint.API,
                            symbol,
                            correlationId);
                        await _timeSyncService.RefreshOffsetAsync();
                        continue;
                    }

                    var canRetry = classification.IsRetryable && attempt <= _retryCount;
                    if (canRetry)
                    {
                        var delay = retryAfterDelay
                                    ?? ((int)response.StatusCode == 418
                                        ? TimeSpan.FromSeconds(30)
                                        : ComputeBackoffDelay(attempt));
                        _logger.LogWarning(
                            "Retrying Binance call after failure. Endpoint={Endpoint}, Method={Method}, Category={Category}, Attempt={Attempt}, DelayMs={DelayMs}, StatusCode={StatusCode}, BinanceCode={BinanceCode}, CorrelationId={CorrelationId}, Symbol={Symbol}",
                            endpoint.API,
                            endpoint.Type,
                            classification.Category,
                            attempt,
                            Math.Round(delay.TotalMilliseconds, 2),
                            statusCode,
                            binanceCode,
                            correlationId,
                            symbol);
                        await Task.Delay(delay);
                        continue;
                    }

                    throw new Exception(
                        $"Binance call failed. Endpoint={endpoint.API}, StatusCode={statusCode}, BinanceCode={binanceCode}, Message={binanceMessage}, Category={classification.Category}");
                }

                _logger.LogInformation(
                    "Binance call succeeded. Endpoint={Endpoint}, Method={Method}, Signed={Signed}, StatusCode={StatusCode}, Attempt={Attempt}, ElapsedMs={ElapsedMs}, CorrelationId={CorrelationId}, Symbol={Symbol}",
                    endpoint.API,
                    endpoint.Type,
                    enableSignature,
                    (int)response.StatusCode,
                    attempt,
                    stopwatch.ElapsedMilliseconds,
                    correlationId,
                    symbol);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<TResponse>(responseBody, options);
                if (result == null)
                    throw new Exception("Failed to deserialize response");

                return result;
            }
            catch (Exception ex) when (IsTransientException(ex) && attempt <= _retryCount)
            {
                var classification = BinanceErrorClassifier.Classify(
                    statusCode: null,
                    binanceCode: null,
                    binanceMessage: ex.Message,
                    exception: ex);
                var delay = ComputeBackoffDelay(attempt);
                _logger.LogWarning(
                    ex,
                    "Binance transport retry. Endpoint={Endpoint}, Method={Method}, Signed={Signed}, Category={Category}, Attempt={Attempt}, DelayMs={DelayMs}, CorrelationId={CorrelationId}, Symbol={Symbol}",
                    endpoint.API,
                    endpoint.Type,
                    enableSignature,
                    classification.Category,
                    attempt,
                    Math.Round(delay.TotalMilliseconds, 2),
                    correlationId,
                    symbol);
                await Task.Delay(delay);
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Binance request timed out after {_requestTimeoutSeconds}s. Endpoint={endpoint.API}", ex);
            }
        }

        throw new Exception($"Binance call failed after retries. Endpoint={endpoint.API}");
    }

    private HttpRequestMessage BuildRequestMessage(Endpoint endpoint, string queryString, bool enableSignature)
    {
        HttpRequestMessage message;
        if (endpoint.Type?.Equals("GET", StringComparison.OrdinalIgnoreCase) == true)
        {
            var fullUrl = string.IsNullOrEmpty(queryString)
                ? endpoint.API
                : $"{endpoint.API}?{queryString}";
            message = new HttpRequestMessage(HttpMethod.Get, fullUrl);
        }
        else if (endpoint.Type?.Equals("POST", StringComparison.OrdinalIgnoreCase) == true)
        {
            message = new HttpRequestMessage(HttpMethod.Post, endpoint.API)
            {
                Content = new StringContent(queryString, Encoding.UTF8, "application/x-www-form-urlencoded")
            };
        }
        else if (endpoint.Type?.Equals("DELETE", StringComparison.OrdinalIgnoreCase) == true)
        {
            var fullUrl = string.IsNullOrEmpty(queryString)
                ? endpoint.API
                : $"{endpoint.API}?{queryString}";
            message = new HttpRequestMessage(HttpMethod.Delete, fullUrl);
        }
        else if (endpoint.Type?.Equals("PUT", StringComparison.OrdinalIgnoreCase) == true)
        {
            message = new HttpRequestMessage(HttpMethod.Put, endpoint.API)
            {
                Content = new StringContent(queryString, Encoding.UTF8, "application/x-www-form-urlencoded")
            };
        }
        else
        {
            throw new NotSupportedException($"HTTP method {endpoint.Type} not supported");
        }

        if (enableSignature && !string.IsNullOrWhiteSpace(_apiKey))
            message.Headers.TryAddWithoutValidation("X-MBX-APIKEY", _apiKey);

        return message;
    }

    private TimeSpan? ResolveRetryAfterDelay(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
            return null;

        var raw = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (int.TryParse(raw, out var seconds))
            return TimeSpan.FromSeconds(Math.Max(1, seconds));
        if (DateTimeOffset.TryParse(raw, out var date))
        {
            var diff = date - DateTimeOffset.UtcNow;
            return diff > TimeSpan.Zero ? diff : TimeSpan.FromSeconds(1);
        }

        return null;
    }

    private TimeSpan ComputeBackoffDelay(int attempt)
    {
        var baseDelay = _retryBaseDelayMs * Math.Pow(2, Math.Max(0, attempt - 1));
        var jitterMs = _jitter.Next(0, _retryBaseDelayMs + 1);
        return TimeSpan.FromMilliseconds(baseDelay + jitterMs);
    }

    private static bool IsTransientException(Exception ex)
    {
        return ex is HttpRequestException
            || ex is IOException
            || ex is TaskCanceledException;
    }

    private static string? TryGetRequestValue(IReadOnlyDictionary<string, string> values, string key)
    {
        foreach (var kv in values)
        {
            if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return null;
    }

    private static (int? Code, string? Message) TryParseBinanceError(string rawBody)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<BinanceErrorResponse>(rawBody);
            return (parsed?.Code, parsed?.Message);
        }
        catch
        {
            return (null, null);
        }
    }

    private sealed class BinanceErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("msg")]
        public string? Message { get; set; }
    }

    private string CreateSignature(string queryString)
    {
        if (string.IsNullOrWhiteSpace(_secretKey))
            throw new InvalidOperationException("Binance secret key is not configured.");

        byte[] keyBytes = Encoding.UTF8.GetBytes(_secretKey);
        byte[] queryBytes = Encoding.UTF8.GetBytes(queryString);

        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(queryBytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
