using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services.Main;

/// <summary>
/// Signed Binance Futures Testnet (USD-M) client. Hardwired to the testnet base URL and
/// testnet keys read from the Eth15TestnetExecution configuration section. Every signed
/// request re-verifies that the base address is a testnet host (defense in depth): it will
/// never sign a request against a mainnet endpoint.
/// </summary>
public sealed class FuturesTestnetClient : IFuturesTestnetClient
{
    private static readonly string[] ForbiddenMainnetHosts = { "fapi.binance.com", "api.binance.com", "dapi.binance.com" };
    private static readonly string[] AllowedTestnetHosts =
    {
        "demo-fapi.binance.com",
        "testnet.binancefuture.com"
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<FuturesTestnetClient> _logger;
    private readonly ITimeSyncService _timeSyncService;
    private readonly string? _apiKey;
    private readonly string? _secretKey;
    private readonly long _recvWindow;

    public FuturesTestnetClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ITimeSyncService timeSyncService,
        ILogger<FuturesTestnetClient> logger)
    {
        _httpClient = httpClient;
        _timeSyncService = timeSyncService;
        _logger = logger;
        var section = configuration.GetSection("Eth15TestnetExecution");
        _apiKey = section["TestnetApiKey"];
        _secretKey = section["TestnetSecretKey"];
        _recvWindow = 60000;

        if (!string.IsNullOrWhiteSpace(_apiKey))
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-MBX-APIKEY", _apiKey);
    }

    public async Task EnsureLeverageAsync(string symbol, int leverage, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var query = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["symbol"] = symbol,
                ["leverage"] = leverage.ToString(CultureInfo.InvariantCulture)
            };

            try
            {
                await SendSignedAsync(HttpMethod.Post, "/fapi/v1/leverage", query, cancellationToken);
                _logger.LogInformation(
                    "FuturesTestnet leverage set. Symbol={Symbol} Leverage={Leverage} Attempt={Attempt}",
                    symbol, leverage, attempt);
                return;
            }
            catch (InvalidOperationException ex) when (attempt < maxAttempts && IsTransientLeverageTimeout(ex))
            {
                var delay = TimeSpan.FromMilliseconds(250 * (1 << (attempt - 1)));
                _logger.LogWarning(
                    ex,
                    "FuturesTestnet leverage update timed out; retrying the idempotent setting. Symbol={Symbol} Leverage={Leverage} Attempt={Attempt}/{MaxAttempts} DelayMs={DelayMs}",
                    symbol, leverage, attempt, maxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransientLeverageTimeout(InvalidOperationException ex)
        => ex.Message.Contains("Status=408", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Status=503", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("code\":-1007", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase);

    public async Task<decimal> GetMarkPriceAsync(string symbol, CancellationToken cancellationToken = default)
    {
        EnsureTestnetHost();
        using var response = await _httpClient.GetAsync($"/fapi/v1/ticker/price?symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"FuturesTestnet price query failed. Status={(int)response.StatusCode} Body={body}");

        using var doc = JsonDocument.Parse(body);
        var priceStr = doc.RootElement.GetProperty("price").GetString();
        return decimal.Parse(priceStr ?? "0", CultureInfo.InvariantCulture);
    }

    public async Task<FuturesTestnetOrderResult> PlaceMarketOrderAsync(
        string symbol,
        OrderSide side,
        decimal quantity,
        bool reduceOnly,
        CancellationToken cancellationToken = default,
        string? positionSide = null,
        string? clientOrderId = null)
    {
        var stableClientOrderId = string.IsNullOrWhiteSpace(clientOrderId)
            ? Guid.NewGuid().ToString("N")
            : clientOrderId.Length <= 36
                ? clientOrderId
                : clientOrderId[..36];
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["symbol"] = symbol,
            ["side"] = side.ToString(),
            ["type"] = "MARKET",
            ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
            // ACK is Binance's default and returns after order acceptance. RESULT waits for
            // the final MARKET fill and can surface -1007 backend timeouts even when the
            // order was accepted. Callers already resolve fills via userTrades/order query.
            ["newOrderRespType"] = "ACK",
            // A stable ID makes an ambiguous POST recoverable without risking a
            // duplicate order when Binance's backend response times out.
            ["newClientOrderId"] = stableClientOrderId
        };
        if (reduceOnly)
            query["reduceOnly"] = "true";
        if (!string.IsNullOrWhiteSpace(positionSide))
            query["positionSide"] = positionSide;
        const int maxSubmitAttempts = 3;
        InvalidOperationException? lastAmbiguousFailure = null;
        for (var attempt = 1; attempt <= maxSubmitAttempts; attempt++)
        {
            try
            {
                var body = await SendSignedAsync(HttpMethod.Post, "/fapi/v1/order", query, cancellationToken);
                return ParseOrderResult(body, symbol, side.ToString());
            }
            catch (InvalidOperationException ex) when (IsAmbiguousOrderSubmissionFailure(ex))
            {
                lastAmbiguousFailure = ex;
                _logger.LogWarning(
                    ex,
                    "FuturesTestnet market-order response was ambiguous; reconciling by client order ID before any retry. Symbol={Symbol} ClientOrderId={ClientOrderId} Attempt={Attempt}/{MaxAttempts}",
                    symbol, stableClientOrderId, attempt, maxSubmitAttempts);

                var recovered = await TryGetOrderByClientOrderIdAsync(
                    symbol,
                    stableClientOrderId,
                    side.ToString(),
                    cancellationToken);
                if (recovered is not null)
                {
                    _logger.LogInformation(
                        "FuturesTestnet recovered an ambiguously submitted market order. Symbol={Symbol} ClientOrderId={ClientOrderId} OrderId={OrderId} Status={Status}",
                        symbol, stableClientOrderId, recovered.OrderId, recovered.Status);
                    return recovered;
                }

                if (attempt == maxSubmitAttempts)
                    break;

                // All reconciliation probes returned Binance -2013, so this exact
                // client order ID is absent and can be submitted again safely.
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"FuturesTestnet market order was not accepted after {maxSubmitAttempts} reconciled attempts. Symbol={symbol} ClientOrderId={stableClientOrderId}",
            lastAmbiguousFailure);
    }

    private async Task<FuturesTestnetOrderResult?> TryGetOrderByClientOrderIdAsync(
        string symbol,
        string clientOrderId,
        string fallbackSide,
        CancellationToken cancellationToken)
    {
        const int maxQueryAttempts = 3;
        for (var attempt = 1; attempt <= maxQueryAttempts; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150 * (1 << (attempt - 1))), cancellationToken);
            try
            {
                var query = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["symbol"] = symbol,
                    ["origClientOrderId"] = clientOrderId
                };
                var body = await SendSignedAsync(HttpMethod.Get, "/fapi/v1/order", query, cancellationToken);
                return ParseOrderResult(body, symbol, fallbackSide);
            }
            catch (InvalidOperationException ex) when (IsOrderNotFound(ex))
            {
                if (attempt == maxQueryAttempts)
                    return null;
            }
            catch (InvalidOperationException ex) when (IsTransientStatusQueryFailure(ex) && attempt < maxQueryAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "FuturesTestnet order-status reconciliation failed transiently; retrying without resubmitting. Symbol={Symbol} ClientOrderId={ClientOrderId} Attempt={Attempt}/{MaxAttempts}",
                    symbol, clientOrderId, attempt, maxQueryAttempts);
            }
            catch (InvalidOperationException ex) when (IsTransientStatusQueryFailure(ex))
            {
                throw new InvalidOperationException(
                    $"FuturesTestnet order submission status remains unknown; refusing to resubmit. Symbol={symbol} ClientOrderId={clientOrderId}",
                    ex);
            }
        }

        return null;
    }

    private static bool IsAmbiguousOrderSubmissionFailure(InvalidOperationException ex)
        => ex.Message.Contains("Status=408", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Status=500", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Status=502", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Status=503", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Status=504", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("code\":-1007", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Bad Gateway", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransientStatusQueryFailure(InvalidOperationException ex)
        => IsAmbiguousOrderSubmissionFailure(ex) ||
           ex.Message.Contains("Status=429", StringComparison.OrdinalIgnoreCase);

    private static bool IsOrderNotFound(InvalidOperationException ex)
        => ex.Message.Contains("code\":-2013", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Order does not exist", StringComparison.OrdinalIgnoreCase);

    public async Task<FuturesTestnetBalance> GetBalanceAsync(string asset, CancellationToken cancellationToken = default)
    {
        var body = await SendSignedAsync(HttpMethod.Get, "/fapi/v2/balance", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken);
        using var doc = JsonDocument.Parse(body);

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (!string.Equals(GetString(el, "asset"), asset, StringComparison.OrdinalIgnoreCase))
                continue;

            return new FuturesTestnetBalance
            {
                Asset = asset,
                WalletBalance = GetDecimal(el, "balance"),
                AvailableBalance = GetDecimal(el, "availableBalance"),
                CrossUnrealizedPnl = GetDecimal(el, "crossUnPnl")
            };
        }

        throw new InvalidOperationException($"FuturesTestnet balance query returned no entry for asset '{asset}'.");
    }

    public async Task<FuturesTestnetCommissionRate> GetCommissionRateAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["symbol"] = symbol
        };

        var body = await SendSignedAsync(HttpMethod.Get, "/fapi/v1/commissionRate", query, cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        return new FuturesTestnetCommissionRate
        {
            Symbol = GetString(root, "symbol") ?? symbol,
            MakerCommissionRate = GetDecimal(root, "makerCommissionRate"),
            TakerCommissionRate = GetDecimal(root, "takerCommissionRate"),
            RpiCommissionRate = root.TryGetProperty("rpiCommissionRate", out var rpi)
                ? ParseDecimal(rpi)
                : null
        };
    }

    public async Task<FuturesTestnetPositionRisk?> GetPositionRiskAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["symbol"] = symbol
        };

        var body = await SendSignedAsync(HttpMethod.Get, "/fapi/v2/positionRisk", query, cancellationToken);
        using var doc = JsonDocument.Parse(body);

        JsonElement? selected = null;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (string.Equals(GetString(el, "symbol"), symbol, StringComparison.OrdinalIgnoreCase))
                {
                    selected = el;
                    break;
                }
            }
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            selected = doc.RootElement;
        }

        if (selected is null)
            return null;

        var root = selected.Value;
        return new FuturesTestnetPositionRisk
        {
            Symbol = GetString(root, "symbol") ?? symbol,
            PositionAmt = GetDecimal(root, "positionAmt"),
            EntryPrice = GetDecimal(root, "entryPrice"),
            MarkPrice = GetDecimal(root, "markPrice"),
            UnrealizedProfit = GetDecimal(root, "unRealizedProfit"),
            PositionSide = GetString(root, "positionSide") ?? "BOTH",
            UpdateTimeMs = GetLong(root, "updateTime")
        };
    }

    public async Task<IReadOnlyList<FuturesTestnetIncome>> GetIncomeAsync(
        string symbol,
        string incomeType,
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["symbol"] = symbol,
            ["incomeType"] = incomeType,
            ["startTime"] = new DateTimeOffset(DateTime.SpecifyKind(startTimeUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["endTime"] = new DateTimeOffset(DateTime.SpecifyKind(endTimeUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture)
        };

        var body = await SendSignedAsync(HttpMethod.Get, "/fapi/v1/income", query, cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var rows = new List<FuturesTestnetIncome>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            rows.Add(new FuturesTestnetIncome
            {
                Symbol = GetString(el, "symbol") ?? symbol,
                IncomeType = GetString(el, "incomeType") ?? incomeType,
                Income = GetDecimal(el, "income"),
                Asset = GetString(el, "asset") ?? string.Empty,
                TimeMs = GetLong(el, "time"),
                Info = GetString(el, "info")
            });
        }

        return rows;
    }

    public async Task<FuturesTestnetOrderResult> GetOrderAsync(
        string symbol,
        long orderId,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["symbol"] = symbol,
            ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture)
        };

        var body = await SendSignedAsync(HttpMethod.Get, "/fapi/v1/order", query, cancellationToken);
        return ParseOrderResult(body, symbol, string.Empty);
    }

    private static FuturesTestnetOrderResult ParseOrderResult(string body, string fallbackSymbol, string fallbackSide)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new FuturesTestnetOrderResult
        {
            OrderId = GetLong(root, "orderId"),
            Symbol = GetString(root, "symbol") ?? fallbackSymbol,
            Side = GetString(root, "side") ?? fallbackSide,
            Status = GetString(root, "status") ?? "NEW",
            ExecutedQty = GetDecimal(root, "executedQty"),
            AvgPrice = GetDecimal(root, "avgPrice"),
            CumQuote = GetDecimal(root, "cumQuote"),
            UpdateTimeMs = GetLong(root, "updateTime")
        };
    }

    public async Task<IReadOnlyList<FuturesTestnetUserTrade>> GetUserTradesAsync(
        string symbol,
        long orderId,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["symbol"] = symbol,
            ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture)
        };

        var body = await SendSignedAsync(HttpMethod.Get, "/fapi/v1/userTrades", query, cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var trades = new List<FuturesTestnetUserTrade>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            trades.Add(new FuturesTestnetUserTrade
            {
                Id = GetLong(el, "id"),
                OrderId = GetLong(el, "orderId"),
                Symbol = GetString(el, "symbol") ?? symbol,
                Side = GetString(el, "side") ?? string.Empty,
                Price = GetDecimal(el, "price"),
                Qty = GetDecimal(el, "qty"),
                QuoteQty = GetDecimal(el, "quoteQty"),
                Commission = GetDecimal(el, "commission"),
                CommissionAsset = GetString(el, "commissionAsset") ?? string.Empty,
                TimeMs = GetLong(el, "time")
            });
        }

        return trades;
    }

    public async Task<IReadOnlyList<FuturesTestnetKline>> GetKlinesAsync(
        string symbol,
        string interval,
        int limit,
        CancellationToken cancellationToken = default)
    {
        EnsureTestnetHost();
        var url = $"/fapi/v1/klines?symbol={Uri.EscapeDataString(symbol)}&interval={Uri.EscapeDataString(interval)}&limit={Math.Clamp(limit, 1, 1500)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"FuturesTestnet klines query failed. Status={(int)response.StatusCode} Body={body}");

        using var doc = JsonDocument.Parse(body);
        var klines = new List<FuturesTestnetKline>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() < 11)
                continue;

            klines.Add(new FuturesTestnetKline
            {
                OpenTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(el[0].GetInt64()).UtcDateTime,
                Open = ParseDecimal(el[1]),
                High = ParseDecimal(el[2]),
                Low = ParseDecimal(el[3]),
                Close = ParseDecimal(el[4]),
                Volume = ParseDecimal(el[5]),
                CloseTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(el[6].GetInt64()).UtcDateTime,
                QuoteVolume = ParseDecimal(el[7]),
                TradeCount = el[8].ValueKind == JsonValueKind.Number ? el[8].GetInt64() : 0,
                TakerBuyBaseVolume = ParseDecimal(el[9])
            });
        }

        return klines.OrderBy(k => k.OpenTimeUtc).ToList();
    }

    public async Task<FuturesTestnetPremiumIndex> GetPremiumIndexAsync(string symbol, CancellationToken cancellationToken = default)
    {
        EnsureTestnetHost();
        using var response = await _httpClient.GetAsync($"/fapi/v1/premiumIndex?symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"FuturesTestnet premiumIndex query failed. Status={(int)response.StatusCode} Body={body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var nextFundingMs = GetLong(root, "nextFundingTime");
        return new FuturesTestnetPremiumIndex
        {
            Symbol = GetString(root, "symbol") ?? symbol,
            MarkPrice = GetDecimal(root, "markPrice"),
            IndexPrice = GetDecimal(root, "indexPrice"),
            LastFundingRate = GetDecimal(root, "lastFundingRate"),
            NextFundingTimeUtc = nextFundingMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(nextFundingMs).UtcDateTime
                : DateTime.UtcNow
        };
    }

    public async Task<FuturesTestnetSymbolFilters> GetSymbolFiltersAsync(string symbol, CancellationToken cancellationToken = default)
    {
        EnsureTestnetHost();
        using var response = await _httpClient.GetAsync($"/fapi/v1/exchangeInfo?symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"FuturesTestnet exchangeInfo query failed. Status={(int)response.StatusCode} Body={body}");

        using var doc = JsonDocument.Parse(body);
        var filters = new FuturesTestnetSymbolFilters { Symbol = symbol };

        if (!doc.RootElement.TryGetProperty("symbols", out var symbols) || symbols.ValueKind != JsonValueKind.Array)
            return filters;

        foreach (var symbolInfo in symbols.EnumerateArray())
        {
            if (!string.Equals(GetString(symbolInfo, "symbol"), symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!symbolInfo.TryGetProperty("filters", out var filterArray) || filterArray.ValueKind != JsonValueKind.Array)
                break;

            foreach (var filter in filterArray.EnumerateArray())
            {
                switch (GetString(filter, "filterType"))
                {
                    case "LOT_SIZE":
                        filters.QuantityStepSize = GetDecimal(filter, "stepSize");
                        filters.MinQuantity = GetDecimal(filter, "minQty");
                        break;
                    case "MIN_NOTIONAL":
                        filters.MinNotional = GetDecimal(filter, "notional");
                        break;
                    case "PRICE_FILTER":
                        filters.PriceTickSize = GetDecimal(filter, "tickSize");
                        break;
                }
            }

            break;
        }

        return filters;
    }

    private static decimal ParseDecimal(JsonElement el)
        => el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m,
            _ => 0m
        };

    private async Task<string> SendSignedAsync(
        HttpMethod method,
        string path,
        Dictionary<string, string> query,
        CancellationToken cancellationToken)
    {
        EnsureTestnetHost();

        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_secretKey))
            throw new InvalidOperationException("FuturesTestnet credentials are not configured.");

        // Use the server-offset-adjusted timestamp (Redis-cached) to avoid -1021 drift errors;
        // fall back to local UTC when the offset is not yet cached.
        long timestampMs;
        try
        {
            timestampMs = await _timeSyncService.GetAdjustedTimestampAsync(cancellationToken);
        }
        catch
        {
            timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        query["timestamp"] = timestampMs.ToString(CultureInfo.InvariantCulture);
        query["recvWindow"] = _recvWindow.ToString(CultureInfo.InvariantCulture);

        var queryString = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var signature = CreateSignature(queryString);
        var signedQuery = $"{queryString}&signature={signature}";

        HttpRequestMessage message;
        if (method == HttpMethod.Get)
        {
            message = new HttpRequestMessage(HttpMethod.Get, $"{path}?{signedQuery}");
        }
        else
        {
            message = new HttpRequestMessage(method, path)
            {
                Content = new StringContent(signedQuery, Encoding.UTF8)
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        }

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "FuturesTestnet signed call failed. Method={Method} Path={Path} Status={Status} Body={Body}",
                method.Method, path, (int)response.StatusCode, body);
            throw new InvalidOperationException($"FuturesTestnet call failed. Method={method.Method} Path={path} Status={(int)response.StatusCode} Body={body}");
        }

        return body;
    }

    private void EnsureTestnetHost()
    {
        var host = _httpClient.BaseAddress?.Host ?? string.Empty;
        if (ForbiddenMainnetHosts.Any(h => string.Equals(host, h, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"FuturesTestnetClient refuses to call mainnet host '{host}'.");
        if (!AllowedTestnetHosts.Any(h => string.Equals(host, h, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"FuturesTestnetClient base host '{host}' is not a recognized Binance Futures Testnet host.");
    }

    private string CreateSignature(string queryString)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(_secretKey!);
        byte[] queryBytes = Encoding.UTF8.GetBytes(queryString);
        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(queryBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long GetLong(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
            return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : 0,
            JsonValueKind.String => long.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 0,
            _ => 0
        };
    }

    private static decimal GetDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
            return 0m;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 0m,
            _ => 0m
        };
    }
}
