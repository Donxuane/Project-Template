using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Services;

public interface ISpotPositionSizingService
{
    Task<SpotPositionSizingResult> ResolveOpenLongQuantityAsync(
        SpotPositionSizingRequest request,
        CancellationToken cancellationToken = default);

    Task<SpotMinNotionalValidationResult> ValidateMinNotionalAsync(
        TradingSymbol symbol,
        decimal quantity,
        decimal price,
        CancellationToken cancellationToken = default);
}
