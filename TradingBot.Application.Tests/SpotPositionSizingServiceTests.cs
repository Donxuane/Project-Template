using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Binance;
using TradingBot.Domain.Models.Trading;
using TradingBot.Domain.Models.TradingEndpoints;
using TradingBot.Percistance.Services.Main;
using Xunit;

namespace TradingBot.Application.Tests;

public class SpotPositionSizingServiceTests
{
    [Fact]
    public async Task BalanceBasedQuantity_RespectsMaxQuoteCap()
    {
        var service = CreateService(
            useBalanceBasedSizing: true,
            availableUsdt: 1000m,
            price: 500m,
            maxQuotePerTrade: 15m,
            minQuotePerTrade: 10m,
            reservedQuoteBalance: 20m,
            quoteAllocationPercent: 2m,
            filters: EthFilters);

        var result = await service.ResolveOpenLongQuantityAsync(CreateRequest(TradingSymbol.ETHUSDT));

        Assert.True(result.IsSuccess);
        Assert.Equal(SpotQuantitySource.BalanceBasedSizing, result.QuantitySource);
        Assert.Equal(15m, result.CappedQuoteAmount);
        Assert.Equal(980m, result.UsableQuoteBalance);
        Assert.Equal(19.6m, result.DesiredQuoteAmount);
        Assert.Equal(0.03m, result.Quantity);
        Assert.Equal(15m, result.FinalNotional);
    }

    [Fact]
    public async Task BalanceBasedQuantity_RespectsReservedBalance()
    {
        var service = CreateService(
            useBalanceBasedSizing: true,
            availableUsdt: 25m,
            price: 500m,
            reservedQuoteBalance: 20m,
            minQuotePerTrade: 10m,
            filters: EthFilters);

        var result = await service.ResolveOpenLongQuantityAsync(CreateRequest(TradingSymbol.ETHUSDT));

        Assert.False(result.IsSuccess);
        Assert.Contains("usable", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5m, result.UsableQuoteBalance);
    }

    [Fact]
    public async Task BalanceBasedQuantity_BelowMinNotional_RejectsWithoutFallback()
    {
        var filters = CloneFilters(EthFilters, minNotional: 15m);
        var service = CreateService(
            useBalanceBasedSizing: true,
            availableUsdt: 30m,
            price: 500m,
            reservedQuoteBalance: 20m,
            minQuotePerTrade: 10m,
            maxQuotePerTrade: 10m,
            quoteAllocationPercent: 100m,
            filters: filters);

        var result = await service.ResolveOpenLongQuantityAsync(CreateRequest(TradingSymbol.ETHUSDT));

        Assert.False(result.IsSuccess);
        Assert.Equal(SpotQuantitySource.BalanceBasedSizing, result.QuantitySource);
        Assert.Contains("minNotional", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BalanceBasedQuantity_AppliesBinanceNormalization()
    {
        var bnbFilters = new BinanceSymbolFilters
        {
            Symbol = "BNBUSDT",
            StepSize = 0.001m,
            MinQty = 0.001m,
            MaxQty = 1000m,
            MinNotional = 5m
        };

        var service = CreateService(
            useBalanceBasedSizing: true,
            availableUsdt: 1000m,
            price: 631.37m,
            maxQuotePerTrade: 50m,
            minQuotePerTrade: 10m,
            reservedQuoteBalance: 20m,
            quoteAllocationPercent: 2m,
            filters: bnbFilters);

        var result = await service.ResolveOpenLongQuantityAsync(CreateRequest(TradingSymbol.BNBUSDT));

        Assert.True(result.IsSuccess);
        Assert.Equal(19.6m, result.CappedQuoteAmount);
        Assert.NotNull(result.RawQuantity);
        Assert.NotNull(result.NormalizedQuantity);
        Assert.Equal(
            BinanceOrderNormalizationService.NormalizeNewOrder(
                new NewOrderRequest
                {
                    Symbol = "BNBUSDT",
                    Side = OrderSide.BUY,
                    Type = OrderTypes.MARKET,
                    Quantity = result.RawQuantity
                },
                bnbFilters,
                631.37m).NormalizedQuantity,
            result.NormalizedQuantity);
    }

    [Fact]
    public async Task ValidateMinNotional_AfterHighVolatilityReduction_RejectsWhenBelowMinNotional()
    {
        var service = CreateService(
            useBalanceBasedSizing: true,
            availableUsdt: 1000m,
            price: 1000m,
            filters: new BinanceSymbolFilters
            {
                Symbol = "SOLUSDT",
                StepSize = 0.001m,
                MinQty = 0.001m,
                MinNotional = 10m
            });

        var validation = await service.ValidateMinNotionalAsync(
            TradingSymbol.SOLUSDT,
            quantity: 0.005m,
            price: 1000m);

        Assert.False(validation.IsValid);
        Assert.Equal(5m, validation.Notional);
        Assert.Equal(10m, validation.MinNotional);
        Assert.Contains("High-volatility reduced quantity", validation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FixedSymbolQuantity_WorksWhenBalanceSizingDisabled()
    {
        var service = CreateService(useBalanceBasedSizing: false);

        var result = await service.ResolveOpenLongQuantityAsync(new SpotPositionSizingRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            GlobalQuantity = 0.01m,
            SymbolQuantities = new Dictionary<TradingSymbol, decimal>
            {
                [TradingSymbol.BNBUSDT] = 0.03m,
                [TradingSymbol.SOLUSDT] = 0.25m
            }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(0.03m, result.Quantity);
        Assert.Equal(SpotQuantitySource.SymbolOverride, result.QuantitySource);
    }

    [Fact]
    public async Task FixedGlobalQuantity_WorksWhenBalanceSizingDisabledAndNoSymbolOverride()
    {
        var service = CreateService(useBalanceBasedSizing: false);

        var result = await service.ResolveOpenLongQuantityAsync(new SpotPositionSizingRequest
        {
            Symbol = TradingSymbol.ETHUSDT,
            GlobalQuantity = 0.01m,
            SymbolQuantities = new Dictionary<TradingSymbol, decimal>
            {
                [TradingSymbol.BNBUSDT] = 0.03m
            }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(0.01m, result.Quantity);
        Assert.Equal(SpotQuantitySource.GlobalFallback, result.QuantitySource);
    }

    private static SpotPositionSizingRequest CreateRequest(TradingSymbol symbol)
    {
        return new SpotPositionSizingRequest
        {
            Symbol = symbol,
            GlobalQuantity = 0.01m,
            SymbolQuantities = new Dictionary<TradingSymbol, decimal>()
        };
    }

    private static readonly BinanceSymbolFilters EthFilters = new()
    {
        Symbol = "ETHUSDT",
        StepSize = 0.001m,
        MinQty = 0.001m,
        MaxQty = 1000m,
        MinNotional = 5m
    };

    private static SpotPositionSizingService CreateService(
        bool useBalanceBasedSizing,
        decimal availableUsdt = 1000m,
        decimal price = 500m,
        decimal maxQuotePerTrade = 50m,
        decimal minQuotePerTrade = 10m,
        decimal reservedQuoteBalance = 20m,
        decimal quoteAllocationPercent = 2m,
        BinanceSymbolFilters? filters = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:Mode"] = "Spot",
                ["Trading:UseBalanceBasedSizing"] = useBalanceBasedSizing ? "true" : "false",
                ["Trading:QuoteAllocationPercentPerTrade"] = quoteAllocationPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Trading:MaxQuotePerTrade"] = maxQuotePerTrade.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Trading:MinQuotePerTrade"] = minQuotePerTrade.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Trading:ReservedQuoteBalance"] = reservedQuoteBalance.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Trading:BalanceAsset"] = "USDT"
            })
            .Build();

        return new SpotPositionSizingService(
            configuration,
            new FakeBalanceRepository(availableUsdt),
            new FakePriceCacheService(price),
            new ConfigurableBinanceOrderNormalizationService(filters ?? EthFilters),
            NullLogger<SpotPositionSizingService>.Instance);
    }

    private sealed class FakeBalanceRepository(decimal availableUsdt) : IBalanceRepository
    {
        public Task<long> InsertAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task UpsertLatestAsync(List<BalanceSnapshot> snapshot, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<BalanceSyncWriteResult> UpsertLatestAndAppendHistoryAsync(
            IReadOnlyList<BalanceSnapshot> snapshots,
            string source,
            string? syncCorrelationId,
            bool forceSnapshot = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new BalanceSyncWriteResult());

        public Task<BalanceSnapshot?> GetLatestByAssetAsync(string asset, Assets assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<BalanceSnapshot?>(new BalanceSnapshot
            {
                Asset = asset,
                AssetId = assetId,
                Free = availableUsdt,
                Locked = 0m
            });

        public Task<BalanceSnapshot?> GetLatestAsync(string asset, TradingSymbol symbol, CancellationToken cancellationToken = default)
            => GetLatestByAssetAsync(asset, Assets.USDT, cancellationToken);

        public Task<IReadOnlyList<BalanceSnapshot>> GetLatestForAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BalanceSnapshot>>([]);

        public Task<IReadOnlyList<BalanceSnapshot>> GetStaleBalancesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BalanceSnapshot>>([]);
    }

    private sealed class FakePriceCacheService(decimal price) : IPriceCacheService
    {
        public Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<decimal?>(price);

        public Task<Domain.Models.MarketData.PriceSnapshot?> GetCachedPriceSnapshotAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<Domain.Models.MarketData.PriceSnapshot?>(null);

        public Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static BinanceSymbolFilters CloneFilters(
        BinanceSymbolFilters source,
        decimal? minNotional = null,
        decimal? stepSize = null,
        decimal? minQty = null)
    {
        return new BinanceSymbolFilters
        {
            Symbol = source.Symbol,
            StepSize = stepSize ?? source.StepSize,
            MinQty = minQty ?? source.MinQty,
            MaxQty = source.MaxQty,
            MarketStepSize = source.MarketStepSize,
            MarketMinQty = source.MarketMinQty,
            MarketMaxQty = source.MarketMaxQty,
            TickSize = source.TickSize,
            MinPrice = source.MinPrice,
            MaxPrice = source.MaxPrice,
            MinNotional = minNotional ?? source.MinNotional,
            MaxNotional = source.MaxNotional
        };
    }

    private sealed class ConfigurableBinanceOrderNormalizationService(BinanceSymbolFilters filters) : IBinanceOrderNormalizationService
    {
        public Task<BinanceSymbolFilters> GetSymbolFiltersAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new BinanceSymbolFilters
            {
                Symbol = symbol,
                StepSize = filters.StepSize,
                MinQty = filters.MinQty,
                MaxQty = filters.MaxQty,
                MarketStepSize = filters.MarketStepSize,
                MarketMinQty = filters.MarketMinQty,
                MarketMaxQty = filters.MarketMaxQty,
                TickSize = filters.TickSize,
                MinPrice = filters.MinPrice,
                MaxPrice = filters.MaxPrice,
                MinNotional = filters.MinNotional,
                MaxNotional = filters.MaxNotional
            });
        }

        public Task<BinanceOrderNormalizationResult> NormalizeNewOrderAsync(
            NewOrderRequest request,
            decimal? marketPrice,
            CancellationToken cancellationToken = default)
        {
            var symbolFilters = new BinanceSymbolFilters
            {
                Symbol = request.Symbol,
                StepSize = filters.StepSize,
                MinQty = filters.MinQty,
                MaxQty = filters.MaxQty,
                MarketStepSize = filters.MarketStepSize,
                MarketMinQty = filters.MarketMinQty,
                MarketMaxQty = filters.MarketMaxQty,
                TickSize = filters.TickSize,
                MinPrice = filters.MinPrice,
                MaxPrice = filters.MaxPrice,
                MinNotional = filters.MinNotional,
                MaxNotional = filters.MaxNotional
            };

            var normalized = BinanceOrderNormalizationService.NormalizeNewOrder(
                request,
                symbolFilters,
                marketPrice);
            return Task.FromResult(normalized);
        }
    }
}
