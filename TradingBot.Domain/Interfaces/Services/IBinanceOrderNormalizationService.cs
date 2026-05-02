using TradingBot.Domain.Models.Binance;
using TradingBot.Domain.Models.TradingEndpoints;

namespace TradingBot.Domain.Interfaces.Services;

public interface IBinanceOrderNormalizationService
{
    Task<BinanceSymbolFilters> GetSymbolFiltersAsync(string symbol, CancellationToken cancellationToken = default);
    Task<BinanceOrderNormalizationResult> NormalizeNewOrderAsync(NewOrderRequest request, decimal? marketPrice, CancellationToken cancellationToken = default);
}
