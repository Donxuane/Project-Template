namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IAtrService
{
    int RequiredPeriods { get; }
    decimal Calculate(
        IReadOnlyList<decimal> highs,
        IReadOnlyList<decimal> lows,
        IReadOnlyList<decimal> closes,
        bool normalize,
        decimal currentPrice);
}
