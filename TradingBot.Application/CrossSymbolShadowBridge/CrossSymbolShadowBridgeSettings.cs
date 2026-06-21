using Microsoft.Extensions.Configuration;

namespace TradingBot.Application.CrossSymbolShadowBridge;

public sealed class CrossSymbolShadowBridgeSettings
{
    public const string SectionName = "CrossSymbolShadowBridge";
    public const string RealOrdersForbiddenError = "CrossSymbolShadowBridgeRealOrdersForbidden";

    public bool Enabled { get; init; }
    public bool DryRunOnly { get; init; } = true;
    public bool AllowOrders { get; init; }
    public bool AllowTestnetOrders { get; init; }
    public bool AllowRealOrders { get; init; }
    public string CandidateInputDirectory { get; init; } = "TradingBot.Backtest/output/cross-symbol-candidate-engine-v2";
    public string? OutputDirectory { get; init; }
    public bool RequireCanEnterTestnetOrderMode { get; init; } = true;
    public bool AllowResearchPromotedShadowOnly { get; init; } = true;
    public int MaxShadowCandidates { get; init; } = 3;
    public decimal MaxTotalShadowNotionalUsdt { get; init; } = 100m;
    public decimal MaxPerCandidateNotionalUsdt { get; init; } = 25m;
    public bool BlockIfExecutionReadyPortfolioEmpty { get; init; } = true;
    public int IntervalSeconds { get; init; } = 300;

    public bool ShadowOnly => DryRunOnly;
    public bool EffectiveAllowOrders => false;
    public bool EffectiveAllowTestnetOrders => false;
    public bool EffectiveAllowRealOrders => false;
    public bool RealOrdersPlaced => false;
    public bool LiveFuturesRecommended => false;
    public bool BacktestOnly => false;

    public static CrossSymbolShadowBridgeSettings Load(IConfiguration configuration, string contentRootPath)
    {
        var section = configuration.GetSection(SectionName);
        var rawAllowRealOrders = section.GetValue("AllowRealOrders", false);
        if (rawAllowRealOrders)
            throw new InvalidOperationException(RealOrdersForbiddenError);

        var rawAllowOrders = section.GetValue("AllowOrders", false);
        if (rawAllowOrders)
        {
            throw new InvalidOperationException(
                "CrossSymbolShadowBridgeAllowOrdersForbidden: AllowOrders must remain false for shadow-only bridge.");
        }

        var candidateDir = section.GetValue<string>("CandidateInputDirectory")
            ?? "TradingBot.Backtest/output/cross-symbol-candidate-engine-v2";

        return new CrossSymbolShadowBridgeSettings
        {
            Enabled = section.GetValue("Enabled", false),
            DryRunOnly = section.GetValue("DryRunOnly", true),
            AllowOrders = false,
            AllowTestnetOrders = false,
            AllowRealOrders = false,
            CandidateInputDirectory = CrossSymbolShadowBridgePathResolver.Resolve(contentRootPath, candidateDir),
            OutputDirectory = section["OutputDirectory"] is { Length: > 0 } outDir
                ? CrossSymbolShadowBridgePathResolver.Resolve(contentRootPath, outDir)
                : null,
            RequireCanEnterTestnetOrderMode = section.GetValue("RequireCanEnterTestnetOrderMode", true),
            AllowResearchPromotedShadowOnly = section.GetValue("AllowResearchPromotedShadowOnly", true),
            MaxShadowCandidates = Math.Max(1, section.GetValue("MaxShadowCandidates", 3)),
            MaxTotalShadowNotionalUsdt = section.GetValue("MaxTotalShadowNotionalUSDT", 100m),
            MaxPerCandidateNotionalUsdt = section.GetValue("MaxPerCandidateNotionalUSDT", 25m),
            BlockIfExecutionReadyPortfolioEmpty = section.GetValue("BlockIfExecutionReadyPortfolioEmpty", true),
            IntervalSeconds = Math.Max(30, section.GetValue("IntervalSeconds", 300))
        };
    }

    public string ResolveOutputDirectory(string contentRootPath)
        => OutputDirectory
           ?? CrossSymbolShadowBridgePathResolver.Resolve(contentRootPath, "output/cross-symbol-shadow-bridge");
}

public static class CrossSymbolShadowBridgePathResolver
{
    public static string Resolve(string contentRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        var fromContent = Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
        if (Directory.Exists(fromContent) || File.Exists(fromContent))
            return fromContent;

        return Path.GetFullPath(Path.Combine(contentRootPath, "..", configuredPath));
    }
}
