using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface IAdaptiveRollingProfitExitRepository
{
    Task<FuturesCommissionRate?> GetLatestCommissionRateAsync(
        string applicationId,
        string executionEnvironment,
        string accountKey,
        TradingSymbol symbol,
        CancellationToken cancellationToken = default);

    Task InsertCommissionRateAsync(FuturesCommissionRate rate, CancellationToken cancellationToken = default);

    Task<AdaptiveRollingProfitExitStateRecord?> GetStateAsync(long positionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdaptiveRollingProfitExitStateRecord>> GetOpenStatesAsync(CancellationToken cancellationToken = default);

    Task UpsertStateAsync(AdaptiveRollingProfitExitStateRecord state, CancellationToken cancellationToken = default);

    Task InsertEvaluationAsync(AdaptiveRollingProfitExitEvaluationRecord evaluation, CancellationToken cancellationToken = default);

    Task UpsertCounterfactualAsync(AdaptiveRollingProfitCounterfactualRecord counterfactual, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdaptiveRollingProfitCounterfactualRecord>> GetActiveCounterfactualsAsync(CancellationToken cancellationToken = default);
}
