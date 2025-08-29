namespace TradingBot.Domain.Models.GeneralApis;

public class SymbolInfo
{
    public string Symbol { get; set; }
    public string Status { get; set; } // TRADING, HALT, BREAK
    public string BaseAsset { get; set; }
    public string QuoteAsset { get; set; }
    public int BaseAssetPrecision { get; set; }
    public int QuoteAssetPrecision { get; set; }
    public int BaseCommissionPrecision { get; set; }
    public int QuoteCommissionPrecision { get; set; }
    public List<string> OrderTypes { get; set; }
    public bool IcebergAllowed { get; set; }
    public bool OcoAllowed { get; set; }
    public bool OtoAllowed { get; set; }
    public bool QuoteOrderQtyMarketAllowed { get; set; }
    public bool AllowTrailingStop { get; set; }
    public bool CancelReplaceAllowed { get; set; }
    public bool AmendAllowed { get; set; }
    public bool PegInstructionsAllowed { get; set; }
    public bool IsSpotTradingAllowed { get; set; }
    public bool IsMarginTradingAllowed { get; set; }
    public List<object> Filters { get; set; }
    public List<string> Permissions { get; set; }
    public List<List<string>> PermissionSets { get; set; }
    public string DefaultSelfTradePreventionMode { get; set; }
    public List<string> AllowedSelfTradePreventionModes { get; set; }
}
