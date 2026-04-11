using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.API;

public class GeneralApi(ITimeSyncService timeSyncService)
{
    public async Task<long> GetServerTime()
    {
        return await timeSyncService.GetAdjustedTimestampAsync();
    } 
}
