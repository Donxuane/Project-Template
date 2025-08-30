namespace TradingBot.Shared.Shared.Models;

public class RateLimitterSettings
{
    public string? Interval {  get; set; }
    public decimal? IntervalNum { get; set; }
    public decimal? Limit {  get; set; }
    public string? RateLimitType { get; set; }
}
