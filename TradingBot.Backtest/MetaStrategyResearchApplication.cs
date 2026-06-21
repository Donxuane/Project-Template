namespace TradingBot.Backtest;

public sealed class MetaStrategyResearchApplication(BacktestSettings settings)
{
    public async Task<MetaStrategyResearchDiagnostics> RunAsync(CancellationToken cancellationToken)
    {
        var outputRoot = Path.GetDirectoryName(settings.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? settings.OutputDirectory;
        var inputDirs = settings.MetaInputDirectories.Count > 0
            ? settings.MetaInputDirectories
            : MetaStrategyResearchImporter.ResolveDefaultInputDirectories(outputRoot);

        if (inputDirs.Count == 0)
            throw new InvalidOperationException(
                "No meta-research input directories found. Provide --meta-input-dirs or place family outputs under TradingBot.Backtest/output.");

        var (records, importReport) = MetaStrategyResearchImporter.ImportAll(
            inputDirs,
            settings.MetaIncludeBlockedCandidates,
            settings.MetaBlockedCandidateCap);

        if (records.Count == 0)
            throw new InvalidOperationException(
                $"No trade records imported from: {string.Join(", ", inputDirs)}");

        var diagnostics = MetaStrategyResearchAggregator.Build(records, importReport);
        await MetaStrategyResearchReportWriter.WriteAsync(settings.OutputDirectory, diagnostics, cancellationToken);
        return diagnostics;
    }
}
