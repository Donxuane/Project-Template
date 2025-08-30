using TradingBot.Domain.Interfaces.ExternalServices;
using TradingBot.Domain.Models;

namespace TradingBot.Percistance.Services;

public class OrderValidator : IOrderValidator
{
    public (decimal price, decimal qty) ValidatedOrder(decimal price, decimal qty, OrderFilters filters)
    {

        price = AdjustPrice(price, filters.PriceFilter);
        qty = AdjustQuantity(qty, filters.LotSize);


        (price, qty) = AdjustNotional(price, qty, filters);


        if (!ValidatePrice(price, filters.PriceFilter))
            throw new Exception("Adjusted price is invalid according to PriceFilter.");

        if (!ValidateQuantity(qty, filters.LotSize))
            throw new Exception("Adjusted quantity is invalid according to LotSizeFilter.");

        if (!ValidateNotional(price, qty, filters.MinNotional))
            throw new Exception("Adjusted order does not meet MinNotional.");

        return (price, qty);
    }

    private static bool ValidatePrice(decimal price, PriceFilter f)
    {
        if (f == null) return true;
        if (price < decimal.Parse(f.MinPrice) || price > decimal.Parse(f.MaxPrice)) return false;

        var steps = (price - decimal.Parse(f.MinPrice)) / decimal.Parse(f.TickSize);
        return steps == Math.Floor(steps);
    }

    private static bool ValidateQuantity(decimal qty, LotSizeFilter f)
    {
        if (f == null) return true;
        if (qty < decimal.Parse(f.MinQty) || qty > decimal.Parse(f.MaxQty)) return false;

        var steps = (qty - decimal.Parse(f.MinQty)) / decimal.Parse(f.StepSize);
        return steps == Math.Floor(steps);
    }

    private static bool ValidateNotional(decimal price, decimal qty, NotionalFilter f)
    {
        if (f == null) return true;
        return price * qty >= decimal.Parse(f.MinNotional);
    }


    private static decimal AdjustPrice(decimal price, PriceFilter f)
    {
        if (f == null) return price;
        var steps = Math.Floor((price - decimal.Parse(f.MinPrice)) / decimal.Parse(f.TickSize));
        var adjusted = decimal.Parse(f.MinPrice) + steps * decimal.Parse(f.TickSize);


        if (adjusted > decimal.Parse(f.MaxPrice)) adjusted = decimal.Parse(f.MaxPrice);
        return adjusted;
    }

    private static decimal AdjustQuantity(decimal qty, LotSizeFilter f)
    {
        if (f == null) return qty;
        var steps = Math.Floor((qty - decimal.Parse(f.MinQty)) / decimal.Parse(f.StepSize));
        var adjusted = decimal.Parse(f.MinQty) + steps * decimal.Parse(f.StepSize);


        if (adjusted > decimal.Parse(f.MaxQty)) adjusted = decimal.Parse(f.MaxQty);
        return adjusted;
    }

    private static (decimal price, decimal qty) AdjustNotional(decimal price, decimal qty, OrderFilters filters)
    {
        if (filters.MinNotional == null) return (price, qty);

        decimal notional = price * qty;
        if (notional >= decimal.Parse(filters.MinNotional.MinNotional)) return (price, qty);


        decimal requiredQty = decimal.Parse(filters.MinNotional.MinNotional) / price;
        qty = AdjustQuantity(requiredQty, filters.LotSize);


        if (filters.LotSize != null && qty > decimal.Parse(filters.LotSize.MaxQty))
            throw new Exception("Cannot meet MinNotional without exceeding MaxQty.");

        return (price, qty);
    }
}
