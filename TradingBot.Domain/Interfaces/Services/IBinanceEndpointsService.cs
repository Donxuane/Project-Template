using TradingBot.Domain.Enums.General;
using TradingBot.Domain.Models.App;

namespace TradingBot.Domain.Interfaces.Services;

public interface IBinanceEndpointsService
{
    public Endpoint GetEndpoint(GeneralApis general);
}
