using System.Text.Json.Serialization;
using TradingBot.Domain.Settings;

namespace TradingBot.Domain.Models;

public class OrderFilters
{
    public PriceFilter PriceFilter { get; set; }
    public LotSizeFilter LotSize { get; set; }
    public NotionalFilter MinNotional { get; set; }
}

public class PriceFilter : Filter
{
    public string MinPrice { get; set; }
    public string MaxPrice { get; set; }
    public string TickSize { get; set; }
}

public class LotSizeFilter: Filter
{
    public string MinQty { get; set; }
    public string MaxQty { get; set; }
    public string StepSize { get; set; }
}

[JsonConverter(typeof(FilterConverter))]
public abstract class Filter
{
    public string FilterType { get; set; }
}


public class IcebergPartsFilter : Filter
{
    public int Limit { get; set; }
}

public class MarketLotSizeFilter : Filter
{
    public string MinQty { get; set; }
    public string MaxQty { get; set; }
    public string StepSize { get; set; }
}

public class TrailingDeltaFilter : Filter
{
    public int MinTrailingAboveDelta { get; set; }
    public int MaxTrailingAboveDelta { get; set; }
    public int MinTrailingBelowDelta { get; set; }
    public int MaxTrailingBelowDelta { get; set; }
}

public class PercentPriceBySideFilter : Filter
{
    public string BidMultiplierUp { get; set; }
    public string BidMultiplierDown { get; set; }
    public string AskMultiplierUp { get; set; }
    public string AskMultiplierDown { get; set; }
    public int AvgPriceMins { get; set; }
}

public class NotionalFilter : Filter
{
    public string MinNotional { get; set; }
    public bool ApplyMinToMarket { get; set; }
    public string MaxNotional { get; set; }
    public bool ApplyMaxToMarket { get; set; }
    public int AvgPriceMins { get; set; }
}

public class MaxNumOrdersFilter : Filter
{
    public int MaxNumOrders { get; set; }
}

public class MaxNumOrderListsFilter : Filter
{
    public int MaxNumOrderLists { get; set; }
}

public class MaxNumAlgoOrdersFilter : Filter
{
    public int MaxNumAlgoOrders { get; set; }
}

public class MaxNumOrderAmendsFilter : Filter
{
    public int MaxNumOrderAmends { get; set; }
}
