namespace TradingBot.Backtest;

/// <summary>Hash-protects sibling frozen incubation tracks from accidental writes.</summary>
public static class ForwardIncubationTrackProtection
{
    public static IReadOnlyList<string> ResolveProtectedFiles(
        string dataDirectory,
        string outputDirectory,
        bool includeBnb5m,
        bool includeSol5m,
        bool includeBnb15m,
        bool includeSol15m)
    {
        var files = new List<string>();
        if (includeBnb5m)
        {
            files.Add(NoPaidDataShortWindowForwardIncubationV1Catalog.FrozenStatePath(dataDirectory));
            files.Add(NoPaidDataShortWindowForwardIncubationV1Catalog.ForwardHistoryPath(dataDirectory));
        }

        if (includeSol5m)
        {
            files.Add(NoPaidDataShortWindowSolForwardIncubationV1Catalog.FrozenStatePath(dataDirectory));
            files.Add(NoPaidDataShortWindowSolForwardIncubationV1Catalog.ForwardHistoryPath(dataDirectory));
        }

        if (includeBnb15m)
        {
            files.Add(NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.FrozenStatePath(dataDirectory));
            files.Add(NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.ForwardHistoryPath(dataDirectory));
        }

        if (includeSol15m)
        {
            files.Add(NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.FrozenStatePath(dataDirectory));
            files.Add(NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.ForwardHistoryPath(dataDirectory));
        }

        var outputParent = Path.GetDirectoryName(Path.GetFullPath(outputDirectory));
        if (outputParent is not null)
        {
            if (includeBnb5m)
                AddReportDirFiles(files, Path.Combine(outputParent, "no-paid-short-window-forward-incubation-v1-run"));
            if (includeSol5m)
                AddReportDirFiles(files, Path.Combine(outputParent, "no-paid-short-window-sol-forward-incubation-v1"));
            if (includeBnb15m)
                AddReportDirFiles(files, Path.Combine(outputParent, "no-paid-short-window-bnb-15m-forward-incubation-v1"));
            if (includeSol15m)
                AddReportDirFiles(files, Path.Combine(outputParent, "no-paid-short-window-sol-15m-forward-incubation-v1"));
        }

        return files
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static Dictionary<string, string> HashFiles(IReadOnlyList<string> files)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!File.Exists(file))
                continue;
            using var stream = File.OpenRead(file);
            result[file] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }

        return result;
    }

    public static bool FilesByteIdentical(
        Dictionary<string, string> before,
        Dictionary<string, string> after)
        => before.Count == after.Count
           && before.All(kv => after.TryGetValue(kv.Key, out var h) && h == kv.Value);

    private static void AddReportDirFiles(List<string> files, string reportDir)
    {
        if (!Directory.Exists(reportDir))
            return;
        files.AddRange(Directory.GetFiles(reportDir, "*", SearchOption.AllDirectories));
    }
}
