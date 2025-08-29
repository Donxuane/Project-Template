using System.Text.Json.Serialization;
using TradingBot.Shared.Shared.Settings;

namespace TradingBot.Domain.Models;

public class Filters
{
    public PriceFilter PriceFilter { get; set; }
    public LotSizeFilter LotSize { get; set; }
    public MinNotionalFilter MinNotional { get; set; }
}

public class PriceFilter
{
    public string MinPrice { get; set; }
    public string MaxPrice { get; set; }
    public string TickSize { get; set; }
}

public class LotSizeFilter
{
    public string MinQty { get; set; }
    public string MaxQty { get; set; }
    public string StepSize { get; set; }
}

public class MinNotionalFilter
{
    public string? MinNotional { get; set; }
    public bool? ApplyToMarket { get; set; }
    public decimal? AvgPriceMins { get; set; }
}