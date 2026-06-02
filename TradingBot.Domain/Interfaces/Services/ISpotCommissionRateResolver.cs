using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Interfaces.Services;

public interface ISpotCommissionRateResolver
{
    Task<SpotCommissionRateResolution> ResolveFeeRatePercentAsync(
        TradingSymbol symbol,
        CancellationToken cancellationToken = default);
}

public sealed class SpotCommissionRateResolution
{
    public decimal FeeRatePercent { get; init; }
    public string FeeRateSource { get; init; } = "UnknownFallback";
}

