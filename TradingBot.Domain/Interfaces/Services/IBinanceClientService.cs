using TradingBot.Domain.Models.App;

namespace TradingBot.Domain.Interfaces.Services;

public interface IBinanceClientService
{
    Task<ServerTimeResponse?> GetServerTime(CancellationToken cancellationToken = default);
}
