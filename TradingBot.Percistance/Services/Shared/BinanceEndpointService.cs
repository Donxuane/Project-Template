using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums.General;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.App;

namespace TradingBot.Percistance.Services.Shared;

public class BinanceEndpointService(IConfiguration configuration) : IBinanceEndpointsService
{

    public Endpoint GetEndpoint(GeneralApis general)
    {
        var data = configuration.GetSection($"GeneralApis:{general}").Get<Endpoint>();
        if (data != null)
            return data;
        throw new Exception("Unable to find endpoint");
    }
}
