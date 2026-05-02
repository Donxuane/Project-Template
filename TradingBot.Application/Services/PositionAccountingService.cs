using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.Services;

public class PositionAccountingService : IPositionAccountingService
{
    public PositionAccountingResult ApplyTrades(
        Position? currentPosition,
        Order order,
        IReadOnlyList<TradeExecution> trades)
    {
        var position = CloneOrCreatePosition(currentPosition, order.Symbol);
        if (trades.Count == 0)
        {
            return new PositionAccountingResult
            {
                Position = position,
                ProcessedTradeCount = 0,
                Reason = "No trades found for TradesSynced order."
            };
        }

        // TODO: Add persistent processed-trade tracking (e.g. PositionProcessedAt on TradeExecution
        // or a dedicated position trade ledger) to guarantee idempotent accounting across retries.
        var uniqueTrades = trades
            .Where(t => t.PositionProcessedAt is null)
            .GroupBy(t => t.ExchangeTradeId ?? t.Id)
            .Select(g => g.First())
            .OrderBy(t => t.ExecutedAt)
            .ThenBy(t => t.Id)
            .ToList();

        var quantity = position.Quantity;
        var averagePrice = position.AveragePrice;
        var realizedDelta = 0m;
        var feeDelta = 0m;
        var processed = 0;
        var opened = false;
        var closed = false;
        var flipped = false;
        decimal? lastClosingPrice = null;
        var skipped = 0;
        var skippedWithoutOpen = 0;

        foreach (var trade in uniqueTrades)
        {
            if (trade.Quantity <= 0m || trade.Price <= 0m)
            {
                skipped++;
                continue;
            }

            // Spot lifecycle safety: never open/extend short from accounting path.
            // Extra SELL fills when no long remains are ignored to prevent negative quantity.
            if (trade.Side == OrderSide.SELL && quantity <= 0m)
            {
                skipped++;
                skippedWithoutOpen++;
                continue;
            }

            var previousQuantity = quantity;
            var signedTradeQuantity = trade.Side == OrderSide.BUY ? trade.Quantity : -trade.Quantity;
            ApplyTrade(
                ref quantity,
                ref averagePrice,
                signedTradeQuantity,
                trade.Price,
                ref realizedDelta,
                ref flipped,
                ref lastClosingPrice);
            processed++;
            feeDelta += ResolveQuoteFee(trade, order.Symbol.ToString());

            if (previousQuantity == 0m && quantity != 0m)
                opened = true;
            if (previousQuantity != 0m && quantity == 0m)
                closed = true;
        }

        // TODO: If fee is reported in non-quote asset, convert to quote asset before applying.
        realizedDelta -= feeDelta;

        position.Quantity = quantity;
        position.AveragePrice = quantity == 0m ? position.AveragePrice : averagePrice;
        position.RealizedPnl += realizedDelta;
        position.Side = DeriveSide(quantity, position.Side, order.Side);
        position.IsOpen = quantity != 0m;

        if (position.IsOpen)
        {
            if (position.OpenedAt is null)
                position.OpenedAt = DateTime.UtcNow;
            position.ClosedAt = null;
            position.ExitPrice = null;
        }
        else
        {
            position.ClosedAt = DateTime.UtcNow;
            position.ExitPrice = lastClosingPrice ?? position.ExitPrice;
        }

        var reason = processed == 0
            ? "No valid trades could be applied."
            : skipped > 0
                ? $"Applied {processed} trades; skipped {skipped} invalid trades (including {skippedWithoutOpen} SELL trades without open long)."
                : "Applied trades successfully.";

        return new PositionAccountingResult
        {
            Position = position,
            RealizedPnlDelta = realizedDelta,
            FeeDelta = feeDelta,
            ProcessedTradeCount = processed,
            PositionOpened = opened,
            PositionClosed = closed,
            PositionFlipped = flipped,
            Reason = reason
        };
    }

    private static decimal ResolveQuoteFee(TradeExecution trade, string symbolText)
    {
        if (trade.Fee <= 0m)
            return 0m;

        var feeAsset = trade.FeeAsset?.Trim();
        if (string.IsNullOrWhiteSpace(feeAsset))
            return trade.Fee;

        var quoteAsset = symbolText.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) ? "USDT" : string.Empty;
        if (string.IsNullOrEmpty(quoteAsset))
            return 0m;

        if (feeAsset.Equals(quoteAsset, StringComparison.OrdinalIgnoreCase))
            return trade.Fee;

        return 0m;
    }

    private static void ApplyTrade(
        ref decimal quantity,
        ref decimal averagePrice,
        decimal signedTradeQuantity,
        decimal tradePrice,
        ref decimal realizedDelta,
        ref bool flipped,
        ref decimal? lastClosingPrice)
    {
        if (quantity == 0m || Math.Sign(quantity) == Math.Sign(signedTradeQuantity))
        {
            var newQuantity = quantity + signedTradeQuantity;
            if (quantity == 0m)
            {
                averagePrice = tradePrice;
            }
            else
            {
                var oldQtyAbs = Math.Abs(quantity);
                var tradeQtyAbs = Math.Abs(signedTradeQuantity);
                var newQtyAbs = Math.Abs(newQuantity);
                averagePrice = newQtyAbs > 0m
                    ? ((averagePrice * oldQtyAbs) + (tradePrice * tradeQtyAbs)) / newQtyAbs
                    : averagePrice;
            }

            quantity = newQuantity;
            return;
        }

        var closingQuantity = Math.Min(Math.Abs(quantity), Math.Abs(signedTradeQuantity));
        if (quantity > 0m)
            realizedDelta += (tradePrice - averagePrice) * closingQuantity;
        else
            realizedDelta += (averagePrice - tradePrice) * closingQuantity;

        lastClosingPrice = tradePrice;

        var remainingTradeQuantity = Math.Abs(signedTradeQuantity) - closingQuantity;
        if (remainingTradeQuantity <= 0m)
        {
            quantity += signedTradeQuantity;
            return;
        }

        // Spot safety: clamp over-close at zero, do not flip into opposite side.
        quantity = 0m;
        flipped = false;
    }

    private static Position CloneOrCreatePosition(Position? current, TradingBot.Domain.Enums.TradingSymbol symbol)
    {
        if (current is null)
        {
            return new Position
            {
                Symbol = symbol,
                Quantity = 0m,
                AveragePrice = 0m,
                RealizedPnl = 0m,
                UnrealizedPnl = 0m,
                IsOpen = false,
                Side = OrderSide.BUY,
                OpenedAt = null,
                ClosedAt = null,
                ExitPrice = null
            };
        }

        return new Position
        {
            Id = current.Id,
            Symbol = current.Symbol,
            Side = current.Side,
            Quantity = current.Quantity,
            AveragePrice = current.AveragePrice,
            StopLossPrice = current.StopLossPrice,
            TakeProfitPrice = current.TakeProfitPrice,
            ExitPrice = current.ExitPrice,
            ExitReason = current.ExitReason,
            OpenedAt = current.OpenedAt,
            ClosedAt = current.ClosedAt,
            RealizedPnl = current.RealizedPnl,
            UnrealizedPnl = current.UnrealizedPnl,
            CreatedAt = current.CreatedAt,
            UpdatedAt = current.UpdatedAt,
            IsOpen = current.IsOpen,
            IsClosing = current.IsClosing
        };
    }

    private static OrderSide DeriveSide(decimal quantity, OrderSide existingSide, OrderSide fallbackSide)
    {
        if (quantity > 0m)
            return OrderSide.BUY;
        if (quantity < 0m)
            return OrderSide.SELL;

        return existingSide == OrderSide.BUY || existingSide == OrderSide.SELL
            ? existingSide
            : fallbackSide;
    }
}
