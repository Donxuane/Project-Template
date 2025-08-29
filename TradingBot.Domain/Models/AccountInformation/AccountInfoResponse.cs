namespace TradingBot.Domain.Models.AccountInformation;

public class AccountInfoResponse
{
    public int MakerCommission { get; set; }
    public int TakerCommission { get; set; }
    public int BuyerCommission { get; set; }
    public int SellerCommission { get; set; }
    public CommissionRates CommissionRates { get; set; } = new();
    public bool CanTrade { get; set; }
    public bool CanWithdraw { get; set; }
    public bool CanDeposit { get; set; }
    public bool Brokered { get; set; }
    public bool RequireSelfTradePrevention { get; set; }
    public bool PreventSor { get; set; }
    public long UpdateTime { get; set; }
    public string AccountType { get; set; } = string.Empty;
    public List<Balance> Balances { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public long Uid { get; set; }
}

public class CommissionRates
{
    public string Maker { get; set; } = string.Empty;
    public string Taker { get; set; } = string.Empty;
    public string Buyer { get; set; } = string.Empty;
    public string Seller { get; set; } = string.Empty;
}

public class Balance
{
    public string Asset { get; set; } = string.Empty;
    public string Free { get; set; } = string.Empty;
    public string Locked { get; set; } = string.Empty;
}
