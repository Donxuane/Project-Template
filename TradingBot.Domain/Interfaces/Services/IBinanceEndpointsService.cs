using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Shared.Shared.Models;

namespace TradingBot.Domain.Interfaces.Services;

public interface IBinanceEndpointsService
{
    public Endpoint GetEndpoint(Account account);
    public Endpoint GetEndpoint(GeneralApis general);
    public Endpoint GetEndpoint(MarketData marketData);
    public Endpoint GetEndpoint(Trading trading);
}
