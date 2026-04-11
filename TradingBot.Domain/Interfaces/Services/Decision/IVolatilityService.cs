using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IVolatilityService
{
    int RequiredPeriods { get; }
    VolatilityAssessment Assess(
        TradingSymbol symbol,
        IReadOnlyList<decimal> closePrices);
}
