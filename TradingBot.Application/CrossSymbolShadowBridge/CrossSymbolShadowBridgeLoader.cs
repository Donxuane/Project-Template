using System.Text.Json;

namespace TradingBot.Application.CrossSymbolShadowBridge;

public static class CrossSymbolShadowBridgeLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static CrossSymbolShadowBridgeInputProbe Probe(CrossSymbolShadowBridgeSettings settings)
    {
        var candidateDir = settings.CandidateInputDirectory;
        var candidatesPath = Path.Combine(candidateDir, "cross-symbol-candidate-engine-v2-candidates.json");
        var summaryPath = Path.Combine(candidateDir, "cross-symbol-candidate-engine-v2-summary.json");
        var executionPortfolioPath = Path.Combine(
            candidateDir,
            "cross-symbol-candidate-engine-v2-execution-ready-portfolio.json");

        var missing = new List<string>();
        if (!Directory.Exists(candidateDir))
            missing.Add(candidateDir);

        if (!File.Exists(candidatesPath))
            missing.Add(candidatesPath);

        if (!File.Exists(summaryPath))
            missing.Add(summaryPath);

        if (!File.Exists(executionPortfolioPath))
            missing.Add(executionPortfolioPath);

        return new CrossSymbolShadowBridgeInputProbe
        {
            CandidateInputDirectory = candidateDir,
            CandidatesFilePath = candidatesPath,
            SummaryFilePath = summaryPath,
            ExecutionPortfolioFilePath = executionPortfolioPath,
            CandidateInputDirectoryExists = Directory.Exists(candidateDir),
            CandidatesFileExists = File.Exists(candidatesPath),
            SummaryFileExists = File.Exists(summaryPath),
            ExecutionPortfolioFileExists = File.Exists(executionPortfolioPath),
            MissingFiles = missing
        };
    }

    public static async Task<CrossSymbolShadowBridgeInputBundle> LoadAsync(
        CrossSymbolShadowBridgeSettings settings,
        CancellationToken cancellationToken)
    {
        var probe = Probe(settings);
        if (!probe.RequiredInputAvailable)
        {
            throw new InvalidOperationException(
                $"CrossSymbolShadowBridge required input files missing: {string.Join(", ", probe.MissingFiles)}");
        }

        var candidateDir = settings.CandidateInputDirectory;
        var candidates = await LoadCandidatesAsync(candidateDir, cancellationToken);
        var summary = await LoadSummaryAsync(candidateDir, cancellationToken);
        var executionPortfolio = await LoadExecutionPortfolioAsync(candidateDir, cancellationToken);

        var outputRoot = Directory.GetParent(candidateDir)?.FullName
            ?? CrossSymbolShadowBridgePathResolver.Resolve(AppContext.BaseDirectory, "..");

        var shadowPath = Path.Combine(outputRoot, "futures-testnet-shadow-run", "futures-testnet-shadow-decisions.json");
        var bottleneckPath = Path.Combine(outputRoot, "frozen-profile-bottleneck-audit", "frozen-profile-bottleneck-audit.json");

        return new CrossSymbolShadowBridgeInputBundle
        {
            CandidateInputDirectory = candidateDir,
            ShadowDecisionsPath = File.Exists(shadowPath) ? shadowPath : null,
            BottleneckAuditPath = File.Exists(bottleneckPath) ? bottleneckPath : null,
            ShadowDecisionsAvailable = File.Exists(shadowPath),
            BottleneckAuditAvailable = File.Exists(bottleneckPath),
            Summary = summary,
            ExecutionReadyPortfolio = executionPortfolio,
            Candidates = candidates
        };
    }

    private static async Task<IReadOnlyList<CrossSymbolShadowBridgeCandidateImport>> LoadCandidatesAsync(
        string candidateDir,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(candidateDir, "cross-symbol-candidate-engine-v2-candidates.json");

        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (doc.RootElement.TryGetProperty("candidates", out var candidatesElement))
        {
            return JsonSerializer.Deserialize<List<CrossSymbolShadowBridgeCandidateImport>>(
                       candidatesElement.GetRawText(), JsonOptions)
                   ?? [];
        }

        return JsonSerializer.Deserialize<List<CrossSymbolShadowBridgeCandidateImport>>(
                   doc.RootElement.GetRawText(), JsonOptions)
               ?? [];
    }

    private static async Task<CrossSymbolShadowBridgeSummaryImport?> LoadSummaryAsync(
        string candidateDir,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(candidateDir, "cross-symbol-candidate-engine-v2-summary.json");
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CrossSymbolShadowBridgeSummaryImport>(stream, JsonOptions, cancellationToken);
    }

    private static async Task<CrossSymbolShadowBridgeExecutionPortfolioImport?> LoadExecutionPortfolioAsync(
        string candidateDir,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(candidateDir, "cross-symbol-candidate-engine-v2-execution-ready-portfolio.json");
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CrossSymbolShadowBridgeExecutionPortfolioImport>(stream, JsonOptions, cancellationToken);
    }
}
