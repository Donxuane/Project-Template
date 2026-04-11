using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AccountInformation;

namespace TradingBot.Application.API;

public class AccountApi(IToolService toolService, ITimeSyncService timeSyncService, ILogger<AccountApi> logger)
{
    public async Task<AccountInfoResponse>? GetAccountInformation(bool omitZeroBalances)
    {
        try
        {
            var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync();
            var accountInformationEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Account.AccoutnInformation);
            var accountInformation = await toolService.BinanceClientService.Call<AccountInfoResponse, AccountInfoRequest>
            (
                new AccountInfoRequest
                {
                    OmitZeroBalances = omitZeroBalances,
                    RecvWindow = 30000,
                    Timestamp = adjustedTimestamp,
                }, accountInformationEndpoint, true
            );
            return accountInformation;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception: {ex},  
                  DateTime: {date}",
                ex.Message,
                DateTime.Now
            );
            return null;
        }
    }
}
