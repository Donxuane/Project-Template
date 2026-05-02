using System.Text.Json.Serialization;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Shared.Shared.Models;

namespace TradingBot.Domain.Models.GeneralApis;

public class PositionView
{
    public long Id { get; set; }
    [JsonIgnore]
    public TradingSymbol Symbol { get; set; }

    public BaseModelDto? SymbolModel
    {
        get
        {
            return Symbol.ToModel();
        }
    }

    [JsonIgnore]
    public OrderSide Side { get; set; }

    public BaseModelDto? SideModel
    {
        get
        {
            return Side.ToModel();
        }
    }

    public decimal Quantity { get; set; }

    public decimal Average_Price { get; set; }

    public decimal Realized_Pnl { get; set; }

    public decimal Unrealized_Pnl { get; set; }

    public bool Is_Open { get; set; }

    public DateTime Created_At { get; set; }

    public DateTime Updated_At { get; set; }

    public decimal? Stop_Loss_Price { get; set; }

    public decimal? Take_Profit_Price { get; set; }

    public decimal? Exit_Price { get; set; }

    public DateTime? Opened_At { get; set; }

    public DateTime? Closed_At { get; set; }

    [JsonIgnore]
    public PositionExitReason? Exit_Reason { get; set; }

    public BaseModelDto? ExitReasonModel
    {
        get
        {
            if (Exit_Reason == null)
                return null;
            return Exit_Reason.Value.ToModel();
        }
    }
}
