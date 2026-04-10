using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;

namespace TradingBot.Percistance.Services.Main;

public class ToolService(
    Func<IBinanceClientService> clientService,
    Func<IBinanceEndpointsService> endpointsService,
    Func<IBinanceSettingsService> settingsService,
    Func<IRedisCacheService> redisCacheService,
    Func<IOrderValidator> orderValidator,
    Func<ISlicerService> slicerService,
    Func<IAICLinetService> aiClinetService) : IToolService
{
    public IBinanceClientService BinanceClientService => clientService();

    public IBinanceEndpointsService BinanceEndpointsService => endpointsService();

    public IBinanceSettingsService BinanceSettingsService => settingsService();

    public IRedisCacheService RedisCacheService => redisCacheService();

    public IOrderValidator OrderValidator => orderValidator();

    public ISlicerService SlicerService => slicerService();

    public IAICLinetService AICLinetService => aiClinetService();
}
