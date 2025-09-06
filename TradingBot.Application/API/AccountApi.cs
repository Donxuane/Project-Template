using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.GeneralApis;

namespace TradingBot.Application.API;

public class AccountApi(IToolService toolService, ILogger<AccountApi> logger)
{
    public async Task<AccountInfoResponse>? GetAccountInformation(bool omitZeroBalances)
    {
        try
        {
            var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
            var serverTime = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyResult>
            (
                null,
                serverTimeEndpoint,
                false
            );
            var accountInformationEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Account.AccoutnInformation);
            var accountInformation = await toolService.BinanceClientService.Call<AccountInfoResponse, AccountInfoRequest>
            (
                new AccountInfoRequest
                {
                    OmitZeroBalances = omitZeroBalances,
                    RecvWindow = 30000,
                    Timestamp = serverTime.ServerTime,
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
