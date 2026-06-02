using System.Globalization;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class HistoricalKlineDataLoader(BacktestSettings settings)
{
    private readonly BinanceKlineBootstrapDownloader _bootstrapDownloader = new();

    public async Task<SymbolValidationResult> LoadAndValidateAsync(TradingSymbol symbol, CancellationToken cancellationToken)
    {
        var dataDir = settings.DataDirectory;
        var symbolText = symbol.ToString();
        var bootstrapJsonPath = Path.Combine(dataDir, $"{symbolText}-1m.json");
        var candidates = new[]
        {
            bootstrapJsonPath,
            Path.Combine(dataDir, $"{symbolText}-1m.csv"),
            Path.Combine(dataDir, $"{symbolText}.json"),
            Path.Combine(dataDir, $"{symbolText}.csv")
        };

        string? existingPath = candidates.FirstOrDefault(File.Exists);
        if (settings.BootstrapMissingData)
        {
            var (startUtc, endUtc) = ResolveBootstrapWindowUtc();
            await _bootstrapDownloader.DownloadAndMergeToJsonAsync(
                symbolText,
                bootstrapJsonPath,
                settings.BootstrapLimit,
                startUtc,
                endUtc,
                cancellationToken);
            existingPath = bootstrapJsonPath;
        }

        if (existingPath is null || !File.Exists(existingPath))
        {
            return new SymbolValidationResult(
                symbol,
                [],
                [new DataQualityIssue("1m", symbol, "error", $"Missing local candle file for {symbolText}.")]);
        }

        var candles = existingPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? await LoadFromCsvAsync(symbol, existingPath, cancellationToken)
            : await LoadFromJsonAsync(symbol, existingPath, cancellationToken);

        return ValidateAndNormalize(symbol, candles);
    }

    private (DateTime? StartUtc, DateTime? EndUtc) ResolveBootstrapWindowUtc()
    {
        if (settings.BootstrapDays.HasValue)
        {
            var end = DateTime.UtcNow;
            var start = end.AddDays(-settings.BootstrapDays.Value);
            return (start, end);
        }

        if (settings.BootstrapStartUtc.HasValue && settings.BootstrapEndUtc.HasValue)
            return (settings.BootstrapStartUtc.Value, settings.BootstrapEndUtc.Value);

        return (null, null);
    }

    private static async Task<IReadOnlyList<KlineCandle>> LoadFromCsvAsync(TradingSymbol requestedSymbol, string path, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var rows = new List<KlineCandle>();
        if (lines.Length == 0)
            return rows;

        var hasHeader = lines[0].Contains("open", StringComparison.OrdinalIgnoreCase) ||
                        lines[0].Contains("time", StringComparison.OrdinalIgnoreCase);
        var start = hasHeader ? 1 : 0;
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 6)
                continue;

            var symbol = requestedSymbol;
            if (parts.Length >= 7 && Enum.TryParse<TradingSymbol>(parts[6], true, out var parsedSymbol))
                symbol = parsedSymbol;

            var openTime = ParseTime(parts[0]);
            var open = ParseDecimal(parts[1]);
            var high = ParseDecimal(parts[2]);
            var low = ParseDecimal(parts[3]);
            var close = ParseDecimal(parts[4]);
            var volume = ParseDecimal(parts[5]);
            if (openTime is null || open is null || high is null || low is null || close is null || volume is null)
                continue;

            rows.Add(new KlineCandle(symbol, openTime.Value, open.Value, high.Value, low.Value, close.Value, volume.Value));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<KlineCandle>> LoadFromJsonAsync(TradingSymbol requestedSymbol, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var rows = new List<KlineCandle>();
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                if (item.GetArrayLength() < 6)
                    continue;

                var openTime = ParseTimeElement(item[0]);
                var open = ParseDecimalElement(item[1]);
                var high = ParseDecimalElement(item[2]);
                var low = ParseDecimalElement(item[3]);
                var close = ParseDecimalElement(item[4]);
                var volume = ParseDecimalElement(item[5]);
                if (openTime is null || open is null || high is null || low is null || close is null || volume is null)
                    continue;

                rows.Add(new KlineCandle(requestedSymbol, openTime.Value, open.Value, high.Value, low.Value, close.Value, volume.Value));
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var symbol = requestedSymbol;
            if (item.TryGetProperty("symbol", out var symbolElement))
            {
                var symbolText = symbolElement.GetString();
                if (!string.IsNullOrWhiteSpace(symbolText) && Enum.TryParse<TradingSymbol>(symbolText, true, out var parsed))
                    symbol = parsed;
            }

            var openTimeRaw = item.TryGetProperty("openTimeUtc", out var openTimeUtcElement)
                ? openTimeUtcElement
                : item.TryGetProperty("openTime", out var openTimeElement)
                    ? openTimeElement
                    : default;
            var openRaw = item.TryGetProperty("open", out var openElement) ? openElement : default;
            var highRaw = item.TryGetProperty("high", out var highElement) ? highElement : default;
            var lowRaw = item.TryGetProperty("low", out var lowElement) ? lowElement : default;
            var closeRaw = item.TryGetProperty("close", out var closeElement) ? closeElement : default;
            var volumeRaw = item.TryGetProperty("volume", out var volumeElement) ? volumeElement : default;

            var openTimeObj = ParseTimeElement(openTimeRaw);
            var openObj = ParseDecimalElement(openRaw);
            var highObj = ParseDecimalElement(highRaw);
            var lowObj = ParseDecimalElement(lowRaw);
            var closeObj = ParseDecimalElement(closeRaw);
            var volumeObj = ParseDecimalElement(volumeRaw);
            if (openTimeObj is null || openObj is null || highObj is null || lowObj is null || closeObj is null || volumeObj is null)
                continue;

            rows.Add(new KlineCandle(symbol, openTimeObj.Value, openObj.Value, highObj.Value, lowObj.Value, closeObj.Value, volumeObj.Value));
        }

        return rows;
    }

    private static SymbolValidationResult ValidateAndNormalize(TradingSymbol expectedSymbol, IReadOnlyList<KlineCandle> raw)
    {
        var issues = new List<DataQualityIssue>();
        if (raw.Count == 0)
        {
            issues.Add(new DataQualityIssue("1m", expectedSymbol, "error", "No candle rows found."));
            return new SymbolValidationResult(expectedSymbol, [], issues);
        }

        var symbolMismatchCount = raw.Count(x => x.Symbol != expectedSymbol);
        if (symbolMismatchCount > 0)
        {
            issues.Add(new DataQualityIssue(
                "1m",
                expectedSymbol,
                "error",
                $"Symbol mismatch detected in {symbolMismatchCount} rows for {expectedSymbol}."));
        }

        var unsorted = false;
        for (var i = 1; i < raw.Count; i++)
        {
            if (raw[i].OpenTimeUtc < raw[i - 1].OpenTimeUtc)
            {
                unsorted = true;
                break;
            }
        }

        if (unsorted)
            issues.Add(new DataQualityIssue("1m", expectedSymbol, "warning", "Candles were unsorted and will be sorted by timestamp."));

        var sorted = raw.OrderBy(x => x.OpenTimeUtc).ToArray();
        var deduped = new List<KlineCandle>(sorted.Length);
        var duplicateCount = 0;
        KlineCandle? prev = null;
        foreach (var candle in sorted)
        {
            if (prev is not null && candle.OpenTimeUtc == prev.OpenTimeUtc)
            {
                duplicateCount++;
                continue;
            }

            deduped.Add(candle with { Symbol = expectedSymbol });
            prev = candle;
        }

        if (duplicateCount > 0)
            issues.Add(new DataQualityIssue("1m", expectedSymbol, "warning", $"Duplicate timestamps removed: {duplicateCount}."));

        var gapCount = 0;
        for (var i = 1; i < deduped.Count; i++)
        {
            var diff = deduped[i].OpenTimeUtc - deduped[i - 1].OpenTimeUtc;
            if (diff == TimeSpan.FromMinutes(1))
                continue;

            if (diff <= TimeSpan.Zero)
                continue;

            var missing = Math.Max(0, (int)Math.Round(diff.TotalMinutes) - 1);
            if (missing > 0)
            {
                gapCount += missing;
                issues.Add(new DataQualityIssue(
                    "1m",
                    expectedSymbol,
                    "warning",
                    $"Gap detected: {deduped[i - 1].OpenTimeUtc:O} -> {deduped[i].OpenTimeUtc:O}, missing={missing}."));
            }
        }

        if (gapCount == 0)
            issues.Add(new DataQualityIssue("1m", expectedSymbol, "info", "No 1m gaps detected."));

        return new SymbolValidationResult(expectedSymbol, deduped, issues);
    }

    private static DateTime? ParseTime(string value)
    {
        if (long.TryParse(value, out var unixMs))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
            }
            catch
            {
                return null;
            }
        }

        if (DateTimeOffset.TryParse(value, out var dto))
            return dto.UtcDateTime;

        return null;
    }

    private static DateTime? ParseTimeElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return ParseTime(element.GetString() ?? string.Empty);
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return null;
    }

    private static decimal? ParseDecimal(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static decimal? ParseDecimalElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => ParseDecimal(element.GetString() ?? string.Empty),
            JsonValueKind.Number when element.TryGetDecimal(out var value) => value,
            _ => null
        };
    }
}
