using TradingBot.Domain.Models.Diagnostics;

namespace TradingBot.Domain.Interfaces.Services;

public interface ITradingHealthDiagnosticsService
{
    Task<TradingRuntimeHealthResult> RunAsync(TimeSpan? maxBalanceAge = null, CancellationToken cancellationToken = default);
}
