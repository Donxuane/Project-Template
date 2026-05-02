using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Utilities;

namespace TradingBot.Percistance.Services.Main;

public class RiskManagementService(
    IConfiguration configuration,
    IPositionRepository positionRepository,
    IOrderRepository orderRepository,
    IBalanceRepository balanceRepository,
    IPriceCacheService priceCacheService,
    ILogger<RiskManagementService> logger) : IRiskManagementService
{
    private decimal MaxPositionQuote => configuration.GetValue<decimal?>("RiskSettings:MaxPositionQuote") ?? 10_000m;
    private decimal MaxOrderQuote => configuration.GetValue<decimal?>("RiskSettings:MaxOrderQuote") ?? 5_000m;
    private decimal MaxExposurePercent => configuration.GetValue<decimal?>("RiskSettings:MaxExposurePercent") ?? 50m;
    private decimal MinOrderQuote => configuration.GetValue<decimal?>("RiskSettings:MinOrderQuote") ?? 5m;
    private decimal ReducedPositionMultiplier => configuration.GetValue<decimal?>("RiskSettings:ReducedPositionMultiplier") ?? 0.5m;
    private bool EnableDailyLossLimit => configuration.GetValue<bool?>("RiskSettings:EnableDailyLossLimit") ?? true;
    private decimal MaxDailyLossQuote => configuration.GetValue<decimal?>("RiskSettings:MaxDailyLossQuote") ?? 100m;
    private int MaxOpenPositions => configuration.GetValue<int?>("RiskSettings:MaxOpenPositions") ?? 3;
    private bool AllowShortSelling => configuration.GetValue<bool?>("RiskSettings:AllowShortSelling") ?? false;
    private int MinimumRiskScore => configuration.GetValue<int?>("RiskSettings:MinimumRiskScore") ?? 60;
    private decimal DefaultStopLossPercent => configuration.GetValue<decimal?>("RiskSettings:DefaultStopLossPercent") ?? 1.0m;
    private decimal DefaultTakeProfitPercent => configuration.GetValue<decimal?>("RiskSettings:DefaultTakeProfitPercent") ?? 2.0m;
    private string QuoteAsset => configuration.GetValue<string>("RiskSettings:QuoteAsset") ?? "USDT";
    private decimal MaxPositionSize => configuration.GetValue<decimal?>("RiskSettings:MaxPositionSize") ?? 1m;

    public async Task<RiskCheckResult> CheckOrderAsync(
        TradingSymbol symbol,
        OrderSide side,
        decimal quantity,
        decimal? price = null,
        CancellationToken cancellationToken = default,
        bool requiresReducedPositionSize = false,
        TradingMode tradingMode = TradingMode.Spot,
        TradeSignal rawSignal = TradeSignal.Hold,
        TradeExecutionIntent executionIntent = TradeExecutionIntent.None)
    {
        var resolvedPrice = price ?? await priceCacheService.GetCachedPriceAsync(symbol, cancellationToken);
        if (!resolvedPrice.HasValue || resolvedPrice <= 0)
        {
            return new RiskCheckResult
            {
                IsAllowed = false,
                Reason = "Price not available from cache. Ensure MarketDataWorker is running.",
                RequiresReducedPositionSize = requiresReducedPositionSize
            };
        }

        return await EvaluateRiskAsync(
            symbol,
            side,
            quantity,
            resolvedPrice.Value,
            requiresReducedPositionSize,
            tradingMode,
            rawSignal,
            executionIntent,
            cancellationToken);
    }

    public Task<RiskCheckResult> ValidateTrade(
        TradingSymbol symbol,
        decimal quantity,
        decimal price,
        OrderSide side,
        CancellationToken cancellationToken = default,
        bool requiresReducedPositionSize = false,
        TradingMode tradingMode = TradingMode.Spot,
        TradeSignal rawSignal = TradeSignal.Hold,
        TradeExecutionIntent executionIntent = TradeExecutionIntent.None)
    {
        return EvaluateRiskAsync(
            symbol,
            side,
            quantity,
            price,
            requiresReducedPositionSize,
            tradingMode,
            rawSignal,
            executionIntent,
            cancellationToken);
    }

    private async Task<RiskCheckResult> EvaluateRiskAsync(
        TradingSymbol symbol,
        OrderSide side,
        decimal quantity,
        decimal price,
        bool requiresReducedPositionSize,
        TradingMode tradingMode,
        TradeSignal rawSignal,
        TradeExecutionIntent executionIntent,
        CancellationToken cancellationToken)
    {
        var effectiveMultiplier = requiresReducedPositionSize
            ? Math.Clamp(ReducedPositionMultiplier, 0.1m, 1m)
            : 1m;
        var effectiveMaxOrderQuote = MaxOrderQuote * effectiveMultiplier;
        var effectiveMaxPositionQuote = MaxPositionQuote * effectiveMultiplier;

        if (quantity <= 0m)
        {
            return CreateBlocked(
                symbol,
                side,
                "Quantity must be greater than zero.",
                quantity,
                price,
                0m,
                0m,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        if (price <= 0m)
        {
            return CreateBlocked(
                symbol,
                side,
                "Price must be greater than zero.",
                quantity,
                price,
                0m,
                0m,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        var balances = await balanceRepository.GetLatestForAllAsync(cancellationToken);
        var existingPosition = await positionRepository.GetOpenPositionAsync(symbol, cancellationToken);
        var openPositions = await positionRepository.GetOpenPositionsAsync(cancellationToken);

        if (EnableDailyLossLimit)
        {
            var dailyRealizedLoss = await GetDailyRealizedLossQuoteAsync(DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
            if (dailyRealizedLoss >= MaxDailyLossQuote)
            {
                return CreateBlocked(
                    symbol,
                    side,
                    $"Daily loss limit reached. Realized loss {dailyRealizedLoss:F4} exceeds MaxDailyLossQuote {MaxDailyLossQuote:F4}.",
                    quantity,
                    price,
                    0m,
                    0m,
                    requiresReducedPositionSize,
                    effectiveMaxOrderQuote,
                    effectiveMaxPositionQuote);
            }
        }

        var hasPositionOnSymbol = openPositions.Any(p => p.Symbol == symbol);
        if (!hasPositionOnSymbol && openPositions.Count >= Math.Max(1, MaxOpenPositions))
        {
            return CreateBlocked(
                symbol,
                side,
                $"Max open positions limit reached ({MaxOpenPositions}).",
                quantity,
                price,
                0m,
                0m,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        var notional = quantity * price;
        if (notional < Math.Max(0m, MinOrderQuote))
        {
            return CreateBlocked(
                symbol,
                side,
                "Order notional is below minimum allowed order size.",
                quantity,
                price,
                0m,
                0m,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        if (notional > effectiveMaxOrderQuote)
        {
            return CreateBlocked(
                symbol,
                side,
                $"Order notional {notional:F4} exceeds max allowed {effectiveMaxOrderQuote:F4}.",
                quantity,
                price,
                0m,
                0m,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        var currentPositionQty = existingPosition?.Quantity ?? 0m;
        if (side == OrderSide.SELL)
        {
            var projectedQty = currentPositionQty - quantity;
            if (projectedQty < 0m && !AllowShortSelling)
            {
                return CreateBlocked(
                    symbol,
                    side,
                    "Short selling is disabled for spot trading.",
                    quantity,
                    price,
                    0m,
                    0m,
                    requiresReducedPositionSize,
                    effectiveMaxOrderQuote,
                    effectiveMaxPositionQuote);
            }

            if (currentPositionQty < quantity)
            {
                var reason = AllowShortSelling
                    ? "Sell quantity exceeds current position quantity for spot trading."
                    : "Short selling is disabled for spot trading.";
                return CreateBlocked(
                    symbol,
                    side,
                    reason,
                    quantity,
                    price,
                    0m,
                    0m,
                    requiresReducedPositionSize,
                    effectiveMaxOrderQuote,
                    effectiveMaxPositionQuote);
            }

            var baseAsset = GetBaseAsset(symbol);
            var availableBase = balances
                .Where(x => x.Asset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase))
                .Sum(x => Math.Max(0m, x.Free));
            if (availableBase < quantity)
            {
                return CreateBlocked(
                    symbol,
                    side,
                    $"Insufficient {baseAsset} balance. Required={quantity:F6}, Available={availableBase:F6}.",
                    quantity,
                    price,
                    0m,
                    0m,
                    requiresReducedPositionSize,
                    effectiveMaxOrderQuote,
                    effectiveMaxPositionQuote);
            }
        }
        else if (side == OrderSide.BUY)
        {
            var availableQuote = balances
                .Where(x => x.Asset.Equals(QuoteAsset, StringComparison.OrdinalIgnoreCase))
                .Sum(x => Math.Max(0m, x.Free));
            if (availableQuote < notional)
            {
                return CreateBlocked(
                    symbol,
                    side,
                    $"Insufficient {QuoteAsset} balance. Required={notional:F6}, Available={availableQuote:F6}.",
                    quantity,
                    price,
                    0m,
                    0m,
                    requiresReducedPositionSize,
                    effectiveMaxOrderQuote,
                    effectiveMaxPositionQuote);
            }
        }

        var projectedQuantity = side == OrderSide.BUY
            ? currentPositionQty + quantity
            : currentPositionQty - quantity;
        if (projectedQuantity > MaxPositionSize)
        {
            return CreateBlocked(
                symbol,
                side,
                $"Projected position size {projectedQuantity:F6} exceeds MaxPositionSize {MaxPositionSize:F6}.",
                quantity,
                price,
                0m,
                0m,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        var projectedPositionNotional = Math.Abs(projectedQuantity) * price;
        if (projectedPositionNotional > effectiveMaxPositionQuote)
        {
            return CreateBlocked(
                symbol,
                side,
                $"Position notional {projectedPositionNotional:F4} exceeds max allowed {effectiveMaxPositionQuote:F4}.",
                quantity,
                price,
                0m,
                0m,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        var accountEquityQuote = await CalculateAccountEquityQuoteAsync(balances, cancellationToken);
        if (accountEquityQuote <= 0m)
        {
            return CreateBlocked(
                symbol,
                side,
                "Account equity in quote asset is not available.",
                quantity,
                price,
                0m,
                0m,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        var exposurePercent = (projectedPositionNotional / accountEquityQuote) * 100m;
        if (exposurePercent > MaxExposurePercent)
        {
            return CreateBlocked(
                symbol,
                side,
                $"Exposure {exposurePercent:F2}% exceeds MaxExposurePercent {MaxExposurePercent:F2}%.",
                quantity,
                price,
                accountEquityQuote,
                exposurePercent,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        var openOrders = await orderRepository.GetOpenOrdersAsync(symbol, null, cancellationToken);
        if (openOrders.Any(o => o.Side == side && o.Price == price && o.Quantity == quantity))
        {
            return CreateBlocked(
                symbol,
                side,
                "Duplicate open order detected for same symbol/side/price/quantity.",
                quantity,
                price,
                accountEquityQuote,
                exposurePercent,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote);
        }

        var riskScore = CalculateRiskScore(
            notional,
            projectedPositionNotional,
            exposurePercent,
            effectiveMaxOrderQuote,
            effectiveMaxPositionQuote,
            requiresReducedPositionSize);

        if (riskScore < Math.Clamp(MinimumRiskScore, 0, 100))
        {
            return CreateBlocked(
                symbol,
                side,
                $"Risk score {riskScore} is below minimum required {MinimumRiskScore}.",
                quantity,
                price,
                accountEquityQuote,
                exposurePercent,
                requiresReducedPositionSize,
                effectiveMaxOrderQuote,
                effectiveMaxPositionQuote,
                riskScore);
        }

        var (stopLossPrice, takeProfitPrice) = CalculateProtectionTargets(side, price, tradingMode, executionIntent);
        var allowedResult = new RiskCheckResult
        {
            IsAllowed = true,
            Reason = "Risk checks passed.",
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice,
            RiskScore = riskScore,
            Notional = notional,
            ExposurePercent = exposurePercent,
            AccountEquityQuote = accountEquityQuote,
            RequiresReducedPositionSize = requiresReducedPositionSize
        };
        LogRiskDecision(
            symbol,
            side,
            quantity,
            allowedResult,
            effectiveMaxOrderQuote,
            effectiveMaxPositionQuote,
            Math.Clamp(MaxExposurePercent, 0m, 100m),
            MinOrderQuote,
            requiresReducedPositionSize,
            tradingMode,
            rawSignal,
            executionIntent);
        return allowedResult;
    }

    private async Task<decimal> CalculateAccountEquityQuoteAsync(IReadOnlyList<Domain.Models.Trading.BalanceSnapshot> balances, CancellationToken cancellationToken)
    {
        var equity = 0m;

        foreach (var balance in balances)
        {
            var total = Math.Max(0m, balance.Free + balance.Locked);
            if (total <= 0m)
                continue;

            if (balance.Asset.Equals(QuoteAsset, StringComparison.OrdinalIgnoreCase))
            {
                equity += total;
                continue;
            }

            if (TryResolveQuotePair(balance.Asset, out var quotePair))
            {
                var conversionPrice = await priceCacheService.GetCachedPriceAsync(quotePair, cancellationToken);
                if (conversionPrice.HasValue && conversionPrice.Value > 0m)
                {
                    equity += total * conversionPrice.Value;
                    continue;
                }
            }

            // TODO: Add a dedicated FX conversion source for assets that do not map to TradingSymbol pairs.
        }

        return equity;
    }

    private async Task<decimal> GetDailyRealizedLossQuoteAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var closedPositions = await positionRepository.GetClosedPositionsAsync(cancellationToken);
        var totalLoss = 0m;

        foreach (var position in closedPositions)
        {
            var closedAt = position.ClosedAt ?? position.UpdatedAt;
            if (DateOnly.FromDateTime(closedAt.ToUniversalTime()) != date)
                continue;

            if (position.RealizedPnl < 0m)
                totalLoss += Math.Abs(position.RealizedPnl);
        }

        // TODO: Consider repository-side aggregation for efficiency:
        // Task<decimal> GetRealizedPnlForDateAsync(DateOnly date, CancellationToken cancellationToken)
        return totalLoss;
    }

    private (decimal? stopLoss, decimal? takeProfit) CalculateProtectionTargets(
        OrderSide side,
        decimal entryPrice,
        TradingMode tradingMode,
        TradeExecutionIntent executionIntent)
    {
        if (tradingMode == TradingMode.Spot && executionIntent == TradeExecutionIntent.CloseLong)
            return (null, null);

        var stopLossFactor = Math.Max(0m, DefaultStopLossPercent) / 100m;
        var takeProfitFactor = Math.Max(0m, DefaultTakeProfitPercent) / 100m;
        var epsilon = Math.Max(entryPrice * 0.0001m, 0.00000001m);

        if (side == OrderSide.BUY)
        {
            var stopLoss = entryPrice * (1m - stopLossFactor);
            var takeProfit = entryPrice * (1m + takeProfitFactor);
            if (stopLoss >= entryPrice)
                stopLoss = entryPrice - epsilon;
            if (takeProfit <= entryPrice)
                takeProfit = entryPrice + epsilon;
            return (stopLoss, takeProfit);
        }

        var sellStopLoss = entryPrice * (1m + stopLossFactor);
        var sellTakeProfit = entryPrice * (1m - takeProfitFactor);
        if (sellStopLoss <= entryPrice)
            sellStopLoss = entryPrice + epsilon;
        if (sellTakeProfit >= entryPrice)
            sellTakeProfit = entryPrice - epsilon;
        return (sellStopLoss, sellTakeProfit);
    }

    private int CalculateRiskScore(
        decimal notional,
        decimal projectedPositionNotional,
        decimal exposurePercent,
        decimal effectiveMaxOrderQuote,
        decimal effectiveMaxPositionQuote,
        bool requiresReducedPositionSize)
    {
        var score = 100;

        if (MaxExposurePercent > 0m && exposurePercent > (MaxExposurePercent * 0.5m))
            score -= 20;

        if (effectiveMaxOrderQuote > 0m && notional > (effectiveMaxOrderQuote * 0.5m))
            score -= 20;

        if (requiresReducedPositionSize)
            score -= 20;

        if (effectiveMaxPositionQuote > 0m && projectedPositionNotional >= (effectiveMaxPositionQuote * 0.9m))
            score -= 40;

        return Math.Clamp(score, 0, 100);
    }

    private static string GetBaseAsset(TradingSymbol symbol)
    {
        var text = symbol.ToString();
        return text.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? text[..^4]
            : text;
    }

    private bool TryResolveQuotePair(string asset, out TradingSymbol symbol)
    {
        var pair = $"{asset}{QuoteAsset}";
        return Enum.TryParse(pair, true, out symbol);
    }

    private RiskCheckResult CreateBlocked(
        TradingSymbol symbol,
        OrderSide side,
        string reason,
        decimal quantity,
        decimal price,
        decimal accountEquityQuote,
        decimal exposurePercent,
        bool requiresReducedPositionSize,
        decimal effectiveMaxOrderQuote,
        decimal effectiveMaxPositionQuote,
        int? riskScore = null,
        TradingMode tradingMode = TradingMode.Spot,
        TradeSignal rawSignal = TradeSignal.Hold,
        TradeExecutionIntent executionIntent = TradeExecutionIntent.None)
    {
        var notional = quantity > 0m && price > 0m ? quantity * price : 0m;
        var blockedResult = new RiskCheckResult
        {
            IsAllowed = false,
            Reason = reason,
            Notional = notional,
            ExposurePercent = exposurePercent,
            AccountEquityQuote = accountEquityQuote,
            RequiresReducedPositionSize = requiresReducedPositionSize,
            RiskScore = riskScore ?? 0
        };
        LogRiskDecision(
            symbol,
            side,
            quantity,
            blockedResult,
            effectiveMaxOrderQuote,
            effectiveMaxPositionQuote,
            Math.Clamp(MaxExposurePercent, 0m, 100m),
            MinOrderQuote,
            requiresReducedPositionSize,
            tradingMode,
            rawSignal,
            executionIntent);
        return blockedResult;
    }

    private void LogRiskDecision(
        TradingSymbol symbol,
        OrderSide side,
        decimal quantity,
        RiskCheckResult result,
        decimal effectiveMaxOrderQuote,
        decimal effectiveMaxPositionQuote,
        decimal maxExposurePercent,
        decimal minOrderQuote,
        bool requiresReducedPositionSize,
        TradingMode tradingMode = TradingMode.Spot,
        TradeSignal rawSignal = TradeSignal.Hold,
        TradeExecutionIntent executionIntent = TradeExecutionIntent.None)
    {
        logger.LogInformation(
            "RiskPayload: Allowed={IsAllowed}, Symbol={Symbol}, Side={Side}, TradingMode={TradingMode}, RawSignal={RawSignal}, ExecutionIntent={ExecutionIntent}, Quantity={Quantity}, Reason={Reason}, RiskScore={RiskScore}, Notional={Notional}, ExposurePercent={ExposurePercent}, AccountEquityQuote={AccountEquityQuote}, MinOrderQuote={MinOrderQuote}, MaxOrderQuote={MaxOrderQuote}, MaxPositionQuote={MaxPositionQuote}, MaxExposurePercent={MaxExposurePercent}, RequiresReducedPositionSize={RequiresReducedPositionSize}",
            result.IsAllowed,
            symbol,
            side,
            tradingMode,
            rawSignal,
            executionIntent,
            BinanceDecimalFormatter.FormatQuantity(quantity),
            result.Reason,
            result.RiskScore,
            BinanceDecimalFormatter.FormatDecimal(result.Notional),
            result.ExposurePercent,
            BinanceDecimalFormatter.FormatDecimal(result.AccountEquityQuote),
            BinanceDecimalFormatter.FormatDecimal(minOrderQuote),
            BinanceDecimalFormatter.FormatDecimal(effectiveMaxOrderQuote),
            BinanceDecimalFormatter.FormatDecimal(effectiveMaxPositionQuote),
            maxExposurePercent,
            requiresReducedPositionSize);
    }
}

