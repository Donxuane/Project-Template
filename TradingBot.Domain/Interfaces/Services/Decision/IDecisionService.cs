using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IDecisionService
{
    Task<DecisionResult> DecideAsync(TradingSymbol symbol, decimal quantity, CancellationToken cancellationToken = default);
}
