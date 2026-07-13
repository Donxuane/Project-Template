using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Analytics;

namespace TradingBot.Domain.Interfaces.Repositories;

/// <summary>
/// Persistence for per-closed-candle SpotFuturesCrossMarketTestnetV1 strategy evaluations
/// (table: spot_futures_cross_market_evaluations).
/// </summary>
public interface ISpotFuturesCrossMarketEvaluationRepository
{
    Task<long> InsertAsync(SpotFuturesCrossMarketEvaluation evaluation, CancellationToken cancellationToken = default);

    /// <summary>Latest evaluated candle open time for restart-safe closed-candle dedup.</summary>
    Task<DateTime?> GetLastEvaluatedCandleOpenTimeAsync(TradingSymbol symbol, string interval, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpotFuturesCrossMarketEvaluation>> GetRecentAsync(TradingSymbol symbol, int limit, CancellationToken cancellationToken = default);
}
