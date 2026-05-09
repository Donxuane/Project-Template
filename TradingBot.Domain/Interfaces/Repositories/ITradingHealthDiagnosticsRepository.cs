using TradingBot.Domain.Models.Diagnostics;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface ITradingHealthDiagnosticsRepository
{
    Task<TradingRuntimeHealthMetrics> CollectMetricsAsync(CancellationToken cancellationToken = default);
}
