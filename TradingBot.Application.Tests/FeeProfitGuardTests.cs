using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.MarketData;
using TradingBot.Domain.Models.Trading;
using Xunit;

namespace TradingBot.Application.Tests;

public class FeeProfitGuardTests
{
    [Fact]
    public async Task SpotOpenLong_Allowed_WhenNetProfitAboveThreshold()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 101m
        });

        Assert.True(result.IsAllowed);
        Assert.True(result.NetExpectedProfitPercent > 0.15m);
    }

    [Fact]
    public async Task SpotOpenLong_Blocked_WhenGrossMoveBelowMinimum()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 100.2m
        });

        Assert.False(result.IsAllowed);
        Assert.Contains("gross move", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotOpenLong_Blocked_WhenNetProfitBelowMinimumAfterFeesAndSpread()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 100.35m
        });

        Assert.False(result.IsAllowed);
        Assert.Contains("net profit", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledFeeGuard_AllowsExecution()
    {
        var guard = CreateGuard(useFeeGuard: false);
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 100.05m
        });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task SpotCloseLong_UsesLongProfitCalculation_NotShortStyle()
    {
        var guard = CreateGuard(openPosition: new Position
        {
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.BUY,
            Quantity = 0.02m,
            AveragePrice = 100m,
            IsOpen = true
        });
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Side = OrderSide.SELL,
            Quantity = 0.01m,
            EntryPrice = 101m
        });

        Assert.True(result.IsAllowed);
        Assert.True(result.GrossExpectedProfitPercent > 0m);
    }

    [Fact]
    public async Task ProtectiveClose_BypassesFeeGuard()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Hold,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Side = OrderSide.SELL,
            Quantity = 0.01m,
            EntryPrice = 99m,
            TargetPrice = 99m,
            IsProtectiveExit = true
        });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task FuturesIntent_RemainsUnsupported()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Futures,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.OpenShort,
            Side = OrderSide.SELL,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 99m
        });

        Assert.False(result.IsAllowed);
        Assert.Contains("not supported", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotOpenLong_LogsStructuredDiagnostics_ForAllowedAndBlocked()
    {
        var logger = new CapturingLogger<FeeProfitGuard>();
        var guard = CreateGuard(logger: logger);

        _ = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 101m,
            TargetSource = "RiskManagementService.TakeProfitPrice",
            Caller = "DecisionWorker"
        });

        _ = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 100.2m,
            TargetSource = "RiskManagementService.TakeProfitPrice",
            Caller = "DecisionWorker"
        });

        var openLongLogs = logger.Entries
            .Where(x => x.Message.Contains("Spot OpenLong evaluation", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.True(openLongLogs.Length >= 2);
        Assert.Contains(openLongLogs, x => x.Get<bool>("Allowed"));
        Assert.Contains(openLongLogs, x => !x.Get<bool>("Allowed"));

        var sample = openLongLogs[0];
        Assert.True(sample.Values.ContainsKey("TargetSource"));
        Assert.True(sample.Values.ContainsKey("GrossExpectedMovePercent"));
        Assert.True(sample.Values.ContainsKey("FeeRatePercent"));
        Assert.True(sample.Values.ContainsKey("FeeRateSource"));
        Assert.True(sample.Values.ContainsKey("EstimatedRoundTripCostPercent"));
        Assert.True(sample.Values.ContainsKey("MinExpectedMovePercent"));
        Assert.True(sample.Values.ContainsKey("MinNetProfitPercent"));
        Assert.True(sample.Values.ContainsKey("ExpectedNetProfitPercent"));
        Assert.True(sample.Values.ContainsKey("RejectionReason"));
        Assert.True(sample.Values.ContainsKey("Caller"));
    }

    private static FeeProfitGuard CreateGuard(bool useFeeGuard = true, Position? openPosition = null, ILogger<FeeProfitGuard>? logger = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:UseFeeGuard"] = useFeeGuard ? "true" : "false",
                ["Trading:FeeRatePercent"] = "0.1",
                ["Trading:EstimatedSpreadPercent"] = "0.05",
                ["Trading:MinExpectedMovePercent"] = "0.3",
                ["Trading:MinNetProfitPercent"] = "0.15"
            })
            .Build();

        return new FeeProfitGuard(
            config,
            new FakePositionRepository(openPosition),
            new FakePriceCacheService(100m),
            new FakeSpotCommissionRateResolver(0.1m, "ConfigFallback"),
            new NoOpExpectedMoveBlockObservability(),
            logger ?? NullLogger<FeeProfitGuard>.Instance);
    }

    private sealed class NoOpExpectedMoveBlockObservability : IFeeProfitGuardExpectedMoveBlockObservability
    {
        public void RecordExpectedMoveBlock(FeeProfitGuardExpectedMoveBlockObservation observation)
        {
        }

        public void FlushAndLog(decimal currentMinExpectedMovePercent, decimal currentMinNetProfitPercent, TimeSpan reportingWindow)
        {
        }
    }

    private sealed class FakeSpotCommissionRateResolver(decimal feeRatePercent, string source) : ISpotCommissionRateResolver
    {
        public Task<SpotCommissionRateResolution> ResolveFeeRatePercentAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(new SpotCommissionRateResolution
            {
                FeeRatePercent = feeRatePercent,
                FeeRateSource = source
            });
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (state is IEnumerable<KeyValuePair<string, object?>> structured)
            {
                foreach (var kv in structured)
                    values[kv.Key] = kv.Value;
            }

            Entries.Add(new LogEntry(message, values));
        }
    }

    private sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> Values)
    {
        public T Get<T>(string key)
        {
            var value = Values[key];
            if (value is T typed)
                return typed;

            return (T)Convert.ChangeType(value!, typeof(T));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose()
        {
        }
    }

    private sealed class FakePositionRepository(Position? openPosition) : IPositionRepository
    {
        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default) => Task.FromResult(position.Id);
        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>(openPosition is null ? [] : [openPosition]);
        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>([]);
        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakePriceCacheService(decimal price) : IPriceCacheService
    {
        public Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult<decimal?>(price);
        public Task<PriceSnapshot?> GetCachedPriceSnapshotAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<PriceSnapshot?>(new PriceSnapshot
            {
                Price = price,
                AsOfUtc = DateTime.UtcNow,
                Source = "RedisTicker"
            });
        public Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
