using TradingBot.Domain.Enums.AI;

namespace TradingBot.Domain.Interfaces.Services;

public interface IAICLinetService
{
    public Task<TResponse> Call<TResponse, TRequest>(TRequest request, AiRequestModels models);
}
