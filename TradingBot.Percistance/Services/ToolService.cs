using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services;

public class ToolService : IToolService
{
    private readonly Func<IBinanceClientService> _clientService;
    private readonly Func<IBinanceEndpointsService> _endpointsService;
    private readonly Func<IBinanceSettingsService> _settingsService;
    private readonly Func<IMemoryCacheService> _memoryCacheService;
    private readonly Func<IOrderValidator> _orderValidator;
    private readonly Func<ISlicerService> _slicerService;
    private readonly Func<IAICLinetService> _aiClinetService;
    public ToolService(Func<IBinanceClientService> clientService,
        Func<IBinanceEndpointsService> endpointsService,
        Func<IBinanceSettingsService> settingsService,
        Func<IMemoryCacheService> memoryCacheService,
        Func<IOrderValidator> orderValidator,
        Func<ISlicerService> slicerService,
        Func<IAICLinetService> aiClientService)
    {
        _clientService = clientService;
        _endpointsService = endpointsService;
        _settingsService = settingsService;
        _memoryCacheService = memoryCacheService;
        _orderValidator = orderValidator;
        _slicerService = slicerService;
        _aiClinetService = aiClientService;
    }
    public IBinanceClientService BinanceClientService => _clientService();

    public IBinanceEndpointsService BinanceEndpointsService => _endpointsService();

    public IBinanceSettingsService BinanceSettingsService => _settingsService();

    public IMemoryCacheService MemoryCacheService => _memoryCacheService();

    public IOrderValidator OrderValidator => _orderValidator();

    public ISlicerService SlicerService => _slicerService();

    public IAICLinetService AICLinetService => _aiClinetService();
}
