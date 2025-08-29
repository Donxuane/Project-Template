namespace TradingBot.Domain.Models.GeneralApis;

public class ExchangeInfoResponse
{
    public string Timezone { get; set; }
    public long ServerTime { get; set; }
    public List<RateLimit> RateLimits { get; set; }
    public List<object> ExchangeFilters { get; set; }
    public List<SymbolInfo> Symbols { get; set; }
    public List<SorInfo> Sors { get; set; }
}
