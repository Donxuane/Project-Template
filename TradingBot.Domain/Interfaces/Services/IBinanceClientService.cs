using TradingBot.Shared.Shared.Models;

namespace TradingBot.Domain.Interfaces.Services;

public interface IBinanceClientService
{
    public Task<TResponse> Call<TResponse, TRequest>(TRequest? request, Endpoint endpoint);
}
