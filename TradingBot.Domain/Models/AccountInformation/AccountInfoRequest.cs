using System.Text.Json.Serialization;
using TradingBot.Shared.Shared.Settings;

namespace TradingBot.Domain.Models.AccountInformation;

public class AccountInfoRequest
{
    [JsonConverter(typeof(LowerCaseJsonConverter))]
    public bool? OmitZeroBalances { get; set; } 
    public long? RecvWindow { get; set; }
    public long Timestamp { get; set; }
}
