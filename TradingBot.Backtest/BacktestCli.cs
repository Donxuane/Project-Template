using Microsoft.Extensions.Configuration;

namespace TradingBot.Backtest;

public sealed record BacktestSettings(
    string AppSettingsPath,
    string DataDirectory,
    string OutputDirectory,
    IReadOnlyList<string> Intervals,
    bool BootstrapMissingData,
    int BootstrapLimit,
    int? BootstrapDays,
    DateTime? BootstrapStartUtc,
    DateTime? BootstrapEndUtc,
    decimal FeeRatePercent,
    decimal EstimatedSpreadPercent,
    decimal SlippagePercent,
    bool ForceCloseAtEnd,
    bool ShowHelp);

public static class BacktestCli
{
    public const string HelpText = """
Usage:
  dotnet run --project TradingBot.Backtest -- [options]

Options:
  --appsettings <path>         Base appsettings path. Default: TradingBot/appsettings.json
  --data-dir <path>            Directory with historical 1m candles.
  --output-dir <path>          Output directory for reports.
  --interval <value>           Single interval: 1m, 3m, or 5m.
  --intervals <v1,v2,...>      Multiple intervals: e.g. 1m,3m,5m.
  --bootstrap <true|false>     Download missing local files from Binance API. Default: false
  --bootstrap-limit <int>      Kline limit for bootstrap call. Default: 1000
  --bootstrap-days <days>      Historical bootstrap window in days (allowed: 7,14,30).
  --bootstrap-start <utc>      UTC start for historical bootstrap (ISO8601).
  --bootstrap-end <utc>        UTC end for historical bootstrap (ISO8601).
  --fee-rate-percent <value>   Fee rate percent for net estimate. Default from appsettings Trading:FeeRatePercent.
  --spread-percent <value>     Estimated spread percent. Default from appsettings Trading:EstimatedSpreadPercent.
  --slippage-percent <value>   Slippage percent applied to fills. Default: 0.00
  --force-close-end <true|false>  Force-close open position at end of data. Default: true
  --help                       Show this help.
""";

    public static BacktestSettings Parse(string[] args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var defaultAppSettings = Path.Combine(repoRoot, "TradingBot", "appsettings.json");
        var defaultDataDir = Path.Combine(repoRoot, "TradingBot.Backtest", "data");
        var defaultOutputRoot = Path.Combine(repoRoot, "TradingBot.Backtest", "output");
        var defaultOutputDir = Path.Combine(defaultOutputRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

        string appSettingsPath = defaultAppSettings;
        string dataDir = defaultDataDir;
        string outputDir = defaultOutputDir;
        IReadOnlyList<string>? intervals = null;
        var bootstrap = false;
        var bootstrapLimit = 1000;
        int? bootstrapDays = null;
        DateTime? bootstrapStartUtc = null;
        DateTime? bootstrapEndUtc = null;
        decimal? feeRatePercent = null;
        decimal? spreadPercent = null;
        var slippagePercent = 0m;
        var forceCloseAtEnd = true;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (i + 1 >= args.Length)
                throw new ArgumentException($"Missing value for argument '{arg}'.");

            var value = args[++i].Trim();
            switch (arg)
            {
                case "--appsettings":
                    appSettingsPath = value;
                    break;
                case "--data-dir":
                    dataDir = value;
                    break;
                case "--output-dir":
                    outputDir = value;
                    break;
                case "--bootstrap":
                    bootstrap = ParseBool(value, arg);
                    break;
                case "--interval":
                    intervals = [NormalizeInterval(value)];
                    break;
                case "--intervals":
                    intervals = ParseIntervals(value);
                    break;
                case "--bootstrap-limit":
                    bootstrapLimit = Math.Clamp(ParseInt(value, arg), 1, 1500);
                    break;
                case "--bootstrap-days":
                    bootstrapDays = ParseBootstrapDays(value);
                    break;
                case "--bootstrap-start":
                    bootstrapStartUtc = ParseUtcDateTime(value, arg);
                    break;
                case "--bootstrap-end":
                    bootstrapEndUtc = ParseUtcDateTime(value, arg);
                    break;
                case "--fee-rate-percent":
                    feeRatePercent = ParseDecimal(value, arg);
                    break;
                case "--spread-percent":
                    spreadPercent = ParseDecimal(value, arg);
                    break;
                case "--slippage-percent":
                    slippagePercent = ParseDecimal(value, arg);
                    break;
                case "--force-close-end":
                    forceCloseAtEnd = ParseBool(value, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        if (!File.Exists(appSettingsPath) && !showHelp)
            throw new FileNotFoundException($"Appsettings file not found: {appSettingsPath}");
        intervals ??= ["1m"];
        ValidateBootstrapWindow(bootstrapDays, bootstrapStartUtc, bootstrapEndUtc);

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(outputDir);

        var baseFee = 0.1m;
        var baseSpread = 0.05m;
        if (File.Exists(appSettingsPath))
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(appSettingsPath, optional: false)
                .Build();
            baseFee = Math.Max(0m, config.GetValue<decimal?>("Trading:FeeRatePercent") ?? baseFee);
            baseSpread = Math.Max(0m, config.GetValue<decimal?>("Trading:EstimatedSpreadPercent") ?? baseSpread);
        }

        return new BacktestSettings(
            appSettingsPath,
            dataDir,
            outputDir,
            intervals,
            bootstrap,
            bootstrapLimit,
            bootstrapDays,
            bootstrapStartUtc,
            bootstrapEndUtc,
            Math.Max(0m, feeRatePercent ?? baseFee),
            Math.Max(0m, spreadPercent ?? baseSpread),
            Math.Max(0m, slippagePercent),
            forceCloseAtEnd,
            showHelp);
    }

    private static bool ParseBool(string value, string argName)
    {
        if (bool.TryParse(value, out var parsed))
            return parsed;
        throw new ArgumentException($"Invalid bool for {argName}: '{value}'.");
    }

    private static int ParseInt(string value, string argName)
    {
        if (int.TryParse(value, out var parsed))
            return parsed;
        throw new ArgumentException($"Invalid integer for {argName}: '{value}'.");
    }

    private static decimal ParseDecimal(string value, string argName)
    {
        if (decimal.TryParse(value, out var parsed))
            return parsed;
        throw new ArgumentException($"Invalid decimal for {argName}: '{value}'.");
    }

    private static IReadOnlyList<string> ParseIntervals(string value)
    {
        var parsed = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeInterval)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (parsed.Length == 0)
            throw new ArgumentException("At least one interval must be provided.");
        return parsed;
    }

    private static string NormalizeInterval(string raw)
    {
        var value = raw.Trim().ToLowerInvariant();
        return value switch
        {
            "1m" => "1m",
            "3m" => "3m",
            "5m" => "5m",
            _ => throw new ArgumentException($"Unsupported interval '{raw}'. Allowed: 1m, 3m, 5m.")
        };
    }

    private static int ParseBootstrapDays(string value)
    {
        if (!int.TryParse(value, out var parsed))
            throw new ArgumentException($"Invalid bootstrap days value '{value}'.");
        if (parsed is not (7 or 14 or 30))
            throw new ArgumentException("bootstrap-days must be one of: 7, 14, 30.");
        return parsed;
    }

    private static DateTime ParseUtcDateTime(string value, string argName)
    {
        if (!DateTimeOffset.TryParse(value, out var dto))
            throw new ArgumentException($"Invalid UTC datetime for {argName}: '{value}'.");
        return dto.UtcDateTime;
    }

    private static void ValidateBootstrapWindow(int? bootstrapDays, DateTime? bootstrapStartUtc, DateTime? bootstrapEndUtc)
    {
        if (bootstrapDays.HasValue && (bootstrapStartUtc.HasValue || bootstrapEndUtc.HasValue))
            throw new ArgumentException("Use either --bootstrap-days OR --bootstrap-start/--bootstrap-end, not both.");

        if (bootstrapStartUtc.HasValue ^ bootstrapEndUtc.HasValue)
            throw new ArgumentException("Both --bootstrap-start and --bootstrap-end must be provided together.");

        if (bootstrapStartUtc.HasValue && bootstrapEndUtc.HasValue && bootstrapEndUtc.Value <= bootstrapStartUtc.Value)
            throw new ArgumentException("bootstrap-end must be greater than bootstrap-start.");
    }
}
