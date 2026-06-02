using System.Text.Json;
using TradingBot.Backtest;

var settings = BacktestCli.Parse(args);
if (settings.ShowHelp)
{
    Console.WriteLine(BacktestCli.HelpText);
    return 0;
}

var startedAtUtc = DateTime.UtcNow;
Directory.CreateDirectory(settings.OutputDirectory);

var app = new BacktestApplication(settings);
var result = await app.RunAsync(CancellationToken.None);

var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
var metadata = new
{
    startedAtUtc,
    completedAtUtc = DateTime.UtcNow,
    settings.DataDirectory,
    settings.OutputDirectory,
    settings.Intervals,
    settings.BootstrapMissingData,
    settings.BootstrapLimit,
    settings.BootstrapDays,
    settings.BootstrapStartUtc,
    settings.BootstrapEndUtc,
    settings.FeeRatePercent,
    settings.EstimatedSpreadPercent,
    settings.SlippagePercent,
    profileCount = result.Summaries.Count,
    tradeCount = result.Trades.Count,
    blockedEntryCount = result.BlockedEntries.Count
};
await File.WriteAllTextAsync(
    metadataPath,
    JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Backtest completed. Output: {settings.OutputDirectory}");
Console.WriteLine($"Profiles: {result.Summaries.Count}, Trades: {result.Trades.Count}, Blocked entries: {result.BlockedEntries.Count}");
if (settings.Intervals.Count > 1)
{
    Console.WriteLine("Per-interval reports written under interval folders (1m/3m/5m).");
    Console.WriteLine($"Cross-interval summary JSON: {Path.Combine(settings.OutputDirectory, "cross-interval-summary.json")}");
    Console.WriteLine($"Aggregation diagnostics JSON: {Path.Combine(settings.OutputDirectory, "aggregation-diagnostics.json")}");
}
else
{
    Console.WriteLine($"Summary JSON: {Path.Combine(settings.OutputDirectory, "summary.json")}");
    Console.WriteLine($"Trades JSON: {Path.Combine(settings.OutputDirectory, "trades.json")}");
    Console.WriteLine($"Blocked entries JSON: {Path.Combine(settings.OutputDirectory, "blocked-entries.json")}");
    Console.WriteLine($"Validation JSON: {Path.Combine(settings.OutputDirectory, "data-quality.json")}");
}
return 0;
