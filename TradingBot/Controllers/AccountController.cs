using Microsoft.AspNetCore.Mvc;
using TradingBot.Application.API;
using TradingBot.Domain.Models.AccountInformation;
namespace TradingBot.Controllers;

[ApiController]
[Route("[controller]")]
public class AccountController(AccountApi api) : ControllerBase
{
    [HttpGet("accountInformation")]

    public async Task<ActionResult<AccountInfoResponse>> GetAccountInformation(bool OmitZeroBalances) =>
        await api.GetAccountInformation(OmitZeroBalances);
}
