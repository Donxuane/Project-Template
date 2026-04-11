using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface ICandleWarmupService
{
    bool IsWarmedUp { get; }
    Task WarmUpAsync(IReadOnlyList<TradingSymbol> symbols, CancellationToken cancellationToken = default);
}
