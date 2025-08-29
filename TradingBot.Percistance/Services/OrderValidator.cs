//using TradingBot.Domain.Interfaces.Services;
//using TradingBot.Domain.Models;

//namespace TradingBot.Percistance.Services;

//public class OrderValidator : IOrderValidator
//{
//    public (decimal price, decimal qty) ValidatedOrder(decimal price, decimal qty, Filters filters)
//    {
       
//        price = AdjustPrice(price, filters.PriceFilter);
//        qty = AdjustQuantity(qty, filters.LotSize);

       
//        (price, qty) = AdjustNotional(price, qty, filters);

       
//        if (!ValidatePrice(price, filters.PriceFilter))
//            throw new Exception("Adjusted price is invalid according to PriceFilter.");

//        if (!ValidateQuantity(qty, filters.LotSize))
//            throw new Exception("Adjusted quantity is invalid according to LotSizeFilter.");

//        if (!ValidateNotional(price, qty, filters.MinNotional))
//            throw new Exception("Adjusted order does not meet MinNotional.");

//        return (price, qty);
//    }

//    private static bool ValidatePrice(decimal price, PriceFilter f)
//    {
//        if (f == null) return true;
//        if (price < f.MinPrice || price > f.MaxPrice) return false;

//        var steps = (price - f.MinPrice) / f.TickSize;
//        return steps == Math.Floor(steps);
//    }

//    private static bool ValidateQuantity(decimal qty, LotSizeFilter f)
//    {
//        if (f == null) return true;
//        if (qty < f.MinQty || qty > f.MaxQty) return false;

//        var steps = (qty - f.MinQty) / f.StepSize;
//        return steps == Math.Floor(steps);
//    }

//    private static bool ValidateNotional(decimal price, decimal qty, MinNotionalFilter f)
//    {
//        if (f == null) return true;
//        return price * qty >= decimal.Parse(f.MinNotional);
//    }


//    private static decimal AdjustPrice(decimal price, PriceFilter f)
//    {
//        if (f == null) return price;
//        var steps = Math.Floor((price - f.MinPrice) / f.TickSize);
//        var adjusted = f.MinPrice + steps * f.TickSize;

        
//        if (adjusted > f.MaxPrice) adjusted = f.MaxPrice;
//        return adjusted;
//    }

//    private static decimal AdjustQuantity(decimal qty, LotSizeFilter f)
//    {
//        if (f == null) return qty;
//        var steps = Math.Floor((qty - f.MinQty) / f.StepSize);
//        var adjusted = f.MinQty + steps * f.StepSize;

        
//        if (adjusted > f.MaxQty) adjusted = f.MaxQty;
//        return adjusted;
//    }

//    private static (decimal price, decimal qty) AdjustNotional(decimal price, decimal qty, Filters filters)
//    {
//        if (filters.MinNotional == null) return (price, qty);

//        decimal notional = price * qty;
//        if (notional >= decimal.Parse(filters.MinNotional.MinNotional)) return (price, qty);

      
//        decimal requiredQty = decimal.Parse(filters.MinNotional.MinNotional) / price;
//        qty = AdjustQuantity(requiredQty, filters.LotSize);

        
//        if (filters.LotSize != null && qty > filters.LotSize.MaxQty)
//            throw new Exception("Cannot meet MinNotional without exceeding MaxQty.");

//        return (price, qty);
//    }
//}
