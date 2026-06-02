using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services;
using Xunit;

namespace TradingBot.Application.Tests;

public class FeeProfitGuardExpectedMoveBlockObservabilityTests
{
    [Fact]
    public void FlushAndLog_AggregatesPerSymbolMetrics()
    {
        var captured = new CapturingLogger<FeeProfitGuardExpectedMoveBlockObservability>();
        var observability = new FeeProfitGuardExpectedMoveBlockObservability(captured);

        observability.RecordExpectedMoveBlock(new FeeProfitGuardExpectedMoveBlockObservation
        {
            Symbol = TradingSymbol.ETHUSDT,
            ExpectedMovePercent = 0.12m,
            ExpectedNetProfitPercent = -0.05m,
            ExpectedTargetSource = "MovingAverageTrendStrategy.NormalTrendExpectedTarget",
            Confidence = 0.81m,
            RejectionReason = "Skipped because expected gross move is below minimum threshold."
        });
        observability.RecordExpectedMoveBlock(new FeeProfitGuardExpectedMoveBlockObservation
        {
            Symbol = TradingSymbol.ETHUSDT,
            ExpectedMovePercent = 0.18m,
            ExpectedNetProfitPercent = 0.01m,
            ExpectedTargetSource = "MovingAverageTrendStrategy.NormalTrendExpectedTarget",
            Confidence = 0.77m,
            RejectionReason = "Skipped because expected gross move is below minimum threshold."
        });
        observability.RecordExpectedMoveBlock(new FeeProfitGuardExpectedMoveBlockObservation
        {
            Symbol = TradingSymbol.SOLUSDT,
            ExpectedMovePercent = 0.09m,
            ExpectedNetProfitPercent = -0.10m,
            ExpectedTargetSource = "MovingAverageTrendStrategy.LowVolBreakoutExpectedTarget",
            RejectionReason = "Skipped because expected gross move is below minimum threshold."
        });

        observability.FlushAndLog(0.20m, 0.08m, TimeSpan.FromMinutes(30));

        Assert.Equal(2, captured.Samples.Count);
        Assert.Single(captured.Samples.Where(x => x.Message.Contains("Symbol=SOLUSDT", StringComparison.Ordinal)));

        var ethSample = captured.Samples.Single(x => x.Message.Contains("Symbol=ETHUSDT", StringComparison.Ordinal));
        Assert.Contains("TotalBlockedCandidates=2", ethSample.Message, StringComparison.Ordinal);
        Assert.Contains("AvgExpectedMovePercent=0.15", ethSample.Message, StringComparison.Ordinal);
        Assert.Contains("MinExpectedMovePercent=0.12", ethSample.Message, StringComparison.Ordinal);
        Assert.Contains("MaxExpectedMovePercent=0.18", ethSample.Message, StringComparison.Ordinal);
        Assert.Contains("AvgExpectedNetProfitPercent=-0.02", ethSample.Message, StringComparison.Ordinal);
        Assert.Contains("AvgConfidence=0.79", ethSample.Message, StringComparison.Ordinal);
        Assert.Contains("CurrentMinExpectedMovePercent=0.20", ethSample.Message, StringComparison.Ordinal);
        Assert.Contains("CurrentMinNetProfitPercent=0.08", ethSample.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Samples { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Samples.Add((logLevel, formatter(state, exception)));
        }
    }
}
