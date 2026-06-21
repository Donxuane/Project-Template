using System.Text.Json;

namespace TradingBot.Backtest;

/// <summary>
/// Tests Coinalyze free API feasibility WITHOUT a paid plan.
/// The free API key (registration required, no payment) is read only from the
/// COINALYZE_API_KEY environment variable and is never written to disk or source control.
/// Without a key, the probe documents the auth requirement and the documented capabilities.
/// </summary>
public sealed class CoinalyzeFeasibilityProbe
{
    public const string ApiKeyEnvVar = "COINALYZE_API_KEY";
    private const string BaseUrl = "https://api.coinalyze.net/v1";
    private const string RateLimitNotes = "Free tier: 40 API calls/min per key (429 + Retry-After when exceeded). Each symbol in a multi-symbol request consumes one call.";
    private const string RetentionNotes = "Documented retention: intraday granularities (1min..12hour) keep only ~1500-2000 datapoints (old data deleted daily); 'daily' granularity is kept indefinitely. Approx: 5min~7d, 15min~20d, 30min~41d, 1hour~83d, daily~years.";
    private const string SymbolNaming = "Binance perp naming: BTCUSDT_PERP.A, ETHUSDT_PERP.A, BNBUSDT_PERP.A, SOLUSDT_PERP.A (exchange code A = Binance; see /future-markets).";
    private const string IntervalOptions = "1min,5min,15min,30min,1hour,2hour,4hour,6hour,12hour,daily";

    private sealed record MetricSpec(string SourceKey, string DisplayName, string Endpoint, bool DirectEndpoint, string Notes);

    private static readonly MetricSpec[] Metrics =
    [
        new("coinalyzeOpenInterest", "Open Interest History", "/open-interest-history", true,
            "OHLC-style OI history per symbol."),
        new("coinalyzeLiquidations", "Liquidation History", "/liquidation-history", true,
            "Long/short liquidation history; not available on Binance free REST at all, so this is the main Coinalyze added value."),
        new("coinalyzeLongShortRatio", "Long/Short Ratio History", "/long-short-ratio-history", true,
            "Long/short ratio history per symbol."),
        new("coinalyzeFunding", "Funding Rate History", "/funding-rate-history", true,
            "Funding rate history (also predicted funding via /predicted-funding-rate-history)."),
        new("coinalyzeBasis", "Basis (perp vs spot)", "/ohlcv-history (derived)", false,
            "No direct basis endpoint; derive basis from /ohlcv-history of the perp market vs the spot market.")
    ];

    public async Task<IReadOnlyList<ShortWindowDataAvailabilityRow>> ProbeAsync(CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        var hasKey = !string.IsNullOrWhiteSpace(apiKey);
        var rows = new List<ShortWindowDataAvailabilityRow>();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (hasKey)
            client.DefaultRequestHeaders.Add("api_key", apiKey);

        string probeStatusBase;
        var symbolsSupported = SymbolNaming;
        if (!hasKey)
        {
            // One unauthenticated request to document the live auth behavior.
            probeStatusBase = await ProbeNoKeyAsync(client, cancellationToken);
        }
        else
        {
            var marketCheck = await CheckFutureMarketsAsync(client, cancellationToken);
            symbolsSupported = marketCheck;
            probeStatusBase = "ApiKeyPresent(env)";
        }

        foreach (var metric in Metrics)
        {
            decimal? observedLookbackDays = null;
            var probeStatus = probeStatusBase;
            if (hasKey && metric.DirectEndpoint)
            {
                await Task.Delay(1600, cancellationToken); // stay well under 40 calls/min
                (observedLookbackDays, probeStatus) = await ProbeHistoryLookbackAsync(client, metric.Endpoint, cancellationToken);
            }

            rows.Add(new ShortWindowDataAvailabilityRow
            {
                Provider = "coinalyze-free",
                Symbol = "BNBUSDT_PERP.A",
                SourceKey = metric.SourceKey,
                DisplayName = metric.DisplayName,
                Endpoint = BaseUrl + metric.Endpoint,
                IntervalOptions = IntervalOptions,
                RequestedInterval = "1hour",
                MaxLookbackDocumented = RetentionNotes,
                MaxLookbackDaysObserved = observedLookbackDays,
                RateLimitNotes = RateLimitNotes,
                SymbolsSupported = symbolsSupported,
                LocalFilePresent = false,
                LocalRecordCount = 0,
                LocalStartUtc = null,
                LocalEndUtc = null,
                LocalSpanDays = 0m,
                UsefulFor7d = true,
                UsefulFor14d = true,
                UsefulFor30d = true,
                UsefulFor365d = false, // intraday granularity is retention-limited; only 'daily' reaches 365d
                ProbeStatus = probeStatus,
                Notes = metric.Notes + " Free API requires a registered (no-payment) key via env var " + ApiKeyEnvVar + "; key is never committed."
            });
        }

        return rows;
    }

    private static async Task<string> ProbeNoKeyAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync($"{BaseUrl}/exchanges", ct);
            return (int)response.StatusCode == 401
                ? "NoApiKey: live probe returned HTTP 401 (key required). Register a free key and set " + ApiKeyEnvVar + " to verify lookback."
                : $"NoApiKey: unauthenticated probe returned HTTP {(int)response.StatusCode}.";
        }
        catch (Exception ex)
        {
            return $"NoApiKey: probe failed ({ex.GetType().Name}: {ex.Message}).";
        }
    }

    private static async Task<string> CheckFutureMarketsAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync($"{BaseUrl}/future-markets", ct);
            if (!response.IsSuccessStatusCode)
                return $"future-markets probe HTTP {(int)response.StatusCode}; documented naming: {SymbolNaming}";
            var raw = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return SymbolNaming;
            var wanted = new[] { "BTCUSDT_PERP.A", "ETHUSDT_PERP.A", "BNBUSDT_PERP.A", "SOLUSDT_PERP.A" };
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("symbol", out var sym))
                    continue;
                var s = sym.GetString();
                if (s is not null && wanted.Contains(s, StringComparer.OrdinalIgnoreCase))
                    found.Add(s);
            }

            return $"Verified Binance perps available: {string.Join(", ", wanted.Where(w => found.Contains(w)))}"
                   + (found.Count < wanted.Length
                       ? $"; missing: {string.Join(", ", wanted.Where(w => !found.Contains(w)))}"
                       : string.Empty);
        }
        catch (Exception ex)
        {
            return $"future-markets probe failed ({ex.GetType().Name}); documented naming: {SymbolNaming}";
        }
    }

    private static async Task<(decimal? LookbackDays, string Status)> ProbeHistoryLookbackAsync(
        HttpClient client, string endpoint, CancellationToken ct)
    {
        try
        {
            var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var fromSec = DateTimeOffset.UtcNow.AddDays(-200).ToUnixTimeSeconds();
            var uri = $"{BaseUrl}{endpoint}?symbols=BNBUSDT_PERP.A&interval=1hour&from={fromSec}&to={nowSec}";
            using var response = await client.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
                return (null, $"HTTP {(int)response.StatusCode} on {endpoint}");
            var raw = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (null, $"Unexpected payload on {endpoint}");
            long earliest = long.MaxValue;
            long latest = 0;
            var points = 0;
            foreach (var symbolBlock in doc.RootElement.EnumerateArray())
            {
                if (!symbolBlock.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var point in history.EnumerateArray())
                {
                    if (!point.TryGetProperty("t", out var tEl) || !tEl.TryGetInt64(out var t))
                        continue;
                    points++;
                    if (t < earliest)
                        earliest = t;
                    if (t > latest)
                        latest = t;
                }
            }

            if (points == 0)
                return (0m, $"OK but empty history on {endpoint}");
            var days = Math.Round((decimal)(latest - earliest) / 86_400m, 2);
            return (days, $"OK: {points} points at 1hour, observed lookback {days}d");
        }
        catch (Exception ex)
        {
            return (null, $"Probe failed on {endpoint} ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
