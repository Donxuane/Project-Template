using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;

namespace TradingBot.Percistance.Services.Main;

public class ToolService(Func<IBinanceClientService> clientService,
 Func<IBinanceEndpointsService> endpointsService,
 Func<IBinanceSettingsService> settingsService,
 Func<ICacheService> memoryCacheService,
 Func<IOrderValidator> orderValidator,
 Func<ISlicerService> slicerService,
 Func<IAICLinetService> aiClinetService,
 Func<IRedisCacheService> redisCacheService) : IToolService
{
    public IBinanceClientService BinanceClientService => clientService();

    public IBinanceEndpointsService BinanceEndpointsService => endpointsService();

    public IBinanceSettingsService BinanceSettingsService => settingsService();

    public ICacheService CacheService => memoryCacheService();

    public IOrderValidator OrderValidator => orderValidator();

    public ISlicerService SlicerService => slicerService();

    public IAICLinetService AICLinetService => aiClinetService();

    public IRedisCacheService RedisCacheService => redisCacheService();
}
