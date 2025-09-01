using Microsoft.Extensions.Logging;

namespace TradingBot.Domain.Interfaces.Services;

public interface IToolService
{
    public IBinanceClientService BinanceClientService { get; }
    public IBinanceEndpointsService BinanceEndpointsService { get; }
    public IBinanceSettingsService BinanceSettingsService { get; }
    public IMemoryCacheService MemoryCacheService { get; }
    public IOrderValidator OrderValidator { get; }
    public ISlicerService SlicerService { get; }
}
