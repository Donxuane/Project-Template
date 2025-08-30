using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.ExternalServices;
using TradingBot.Shared.Shared.Models;

namespace TradingBot.Percistance.ExternalServices;

public class BinanceEndpointService(IConfiguration configuration) : IBinanceEndpointsService
{
    public Endpoint GetEndpoint(Account account)
    {
        var data = configuration.GetSection($"Account:{account}").Get<Endpoint>();
        if (data != null)
            return data;
        throw new Exception("Unable to find endpoint");
    }

    public Endpoint GetEndpoint(GeneralApis general)
    {
        var data = configuration.GetSection($"GeneralApis:{general}").Get<Endpoint>();
        if (data != null)
            return data;
        throw new Exception("Unable to find endpoint");
    }

    public Endpoint GetEndpoint(MarketData marketData)
    {
        var data = configuration.GetSection($"MarketData:{marketData}").Get<Endpoint>();
        if (data != null)
            return data;
        throw new Exception("Unable to find endpoint");
    }

    public Endpoint GetEndpoint(Trading trading)
    {
        var data = configuration.GetSection($"Trading:{trading}").Get<Endpoint>();
        if (data != null)
            return data;
        throw new Exception("Unable to find endpoint");
    }
}
