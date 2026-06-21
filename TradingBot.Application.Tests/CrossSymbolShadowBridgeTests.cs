using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TradingBot.Application.CrossSymbolShadowBridge;
using Xunit;

namespace TradingBot.Application.Tests;

public sealed class CrossSymbolShadowBridgeTests
{
    [Fact]
    public async Task Evaluate_WritesShadowReports_WithNoExecutionReadyCandidates()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var candidateDir = Path.Combine(repoRoot, "TradingBot.Backtest", "output", "cross-symbol-candidate-engine-v2");
        if (!Directory.Exists(candidateDir))
            return;

        var outputDir = Path.Combine(Path.GetTempPath(), "cross-symbol-shadow-bridge-test-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrossSymbolShadowBridge:Enabled"] = "true",
                ["CrossSymbolShadowBridge:DryRunOnly"] = "true",
                ["CrossSymbolShadowBridge:AllowOrders"] = "false",
                ["CrossSymbolShadowBridge:AllowTestnetOrders"] = "false",
                ["CrossSymbolShadowBridge:AllowRealOrders"] = "false",
                ["CrossSymbolShadowBridge:CandidateInputDirectory"] = candidateDir,
                ["CrossSymbolShadowBridge:OutputDirectory"] = outputDir,
                ["CrossSymbolShadowBridge:RequireCanEnterTestnetOrderMode"] = "true",
                ["CrossSymbolShadowBridge:AllowResearchPromotedShadowOnly"] = "true",
                ["CrossSymbolShadowBridge:BlockIfExecutionReadyPortfolioEmpty"] = "true"
            })
            .Build();

        var hostEnvironment = new TestHostEnvironment(repoRoot);
        var service = new CrossSymbolShadowBridgeService(config, hostEnvironment, Microsoft.Extensions.Logging.Abstractions.NullLogger<CrossSymbolShadowBridgeService>.Instance);

        var result = await service.RunCycleAsync(CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("NoExecutionReadyCandidates", result!.Status.Status);
        Assert.Equal(0, result.Status.ExecutionReadyCandidateCount);
        Assert.True(result.Status.ResearchPromotedShadowOnlyCount >= 1);
        Assert.False(result.Decisions.Any(d => d.WouldPlaceOrder));
        Assert.True(File.Exists(Path.Combine(outputDir, "cross-symbol-shadow-bridge-status.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "cross-symbol-shadow-bridge-decisions.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "cross-symbol-shadow-bridge-risk.json")));

        try { Directory.Delete(outputDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Evaluate_WritesInputFilesMissingStatus_WhenCandidatesMissing()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var missingInputDir = Path.Combine(Path.GetTempPath(), "cross-symbol-shadow-bridge-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(missingInputDir);
        var outputDir = Path.Combine(repoRoot, "TradingBot", "output", "cross-symbol-shadow-bridge");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrossSymbolShadowBridge:Enabled"] = "true",
                ["CrossSymbolShadowBridge:DryRunOnly"] = "true",
                ["CrossSymbolShadowBridge:AllowOrders"] = "false",
                ["CrossSymbolShadowBridge:AllowTestnetOrders"] = "false",
                ["CrossSymbolShadowBridge:AllowRealOrders"] = "false",
                ["CrossSymbolShadowBridge:CandidateInputDirectory"] = missingInputDir,
                ["CrossSymbolShadowBridge:OutputDirectory"] = outputDir
            })
            .Build();

        var hostEnvironment = new TestHostEnvironment(Path.Combine(repoRoot, "TradingBot"));
        var service = new CrossSymbolShadowBridgeService(
            config,
            hostEnvironment,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CrossSymbolShadowBridgeService>.Instance);

        var result = await service.RunCycleAsync(CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("InputFilesMissing", result!.Status.Status);
        Assert.False(result.Status.RealOrdersPlaced);
        Assert.False(result.Status.LiveFuturesRecommended);
        Assert.True(File.Exists(Path.Combine(outputDir, "cross-symbol-shadow-bridge-status.json")));

        try { Directory.Delete(missingInputDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Load_RejectsAllowRealOrders()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrossSymbolShadowBridge:AllowRealOrders"] = "true"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrossSymbolShadowBridgeSettings.Load(config, AppContext.BaseDirectory));

        Assert.Equal(CrossSymbolShadowBridgeSettings.RealOrdersForbiddenError, ex.Message);
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "TradingBot.Application.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
