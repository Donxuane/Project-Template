namespace TradingBot.Domain.Models.Diagnostics;

public sealed class TradingRuntimeHealthResult
{
    public bool IsHealthy { get; set; }
    public DateTime CheckedAt { get; set; }
    public IReadOnlyList<string> CriticalIssues { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public TradingRuntimeHealthIssueCounts Counts { get; set; } = new();
}
