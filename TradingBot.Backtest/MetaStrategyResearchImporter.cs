using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class MetaStrategyResearchImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static (IReadOnlyList<MetaStrategyResearchRecord> Records, MetaStrategyResearchImportReport Report) ImportAll(
        IReadOnlyList<string> inputDirectories,
        bool includeBlockedCandidates,
        int blockedCandidateCap)
    {
        var sources = new List<MetaStrategyResearchImportSourceReport>();
        var records = new List<MetaStrategyResearchRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inputPath in inputDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(inputPath))
            {
                ImportFromDirectory(inputPath, includeBlockedCandidates, blockedCandidateCap, records, seen, sources);
                continue;
            }

            var zipPath = ResolveZipPath(inputPath);
            if (zipPath is not null)
            {
                ImportFromZip(zipPath, includeBlockedCandidates, blockedCandidateCap, records, seen, sources);
                continue;
            }

            sources.Add(new MetaStrategyResearchImportSourceReport(
                inputPath, "Missing", string.Empty, 0, 0, "Directory or zip archive not found", "Missing"));
        }

        var report = new MetaStrategyResearchImportReport(
            inputDirectories,
            sources,
            includeBlockedCandidates,
            blockedCandidateCap);

        return (records, report);
    }

    public static IReadOnlyList<string> ResolveDefaultInputDirectories(string outputRoot)
    {
        var names = new[]
        {
            "range-expansion-v2-feasibility-bnb",
            "range-expansion-v23-bnb",
            "range-expansion-v24-bnb",
            "range-expansion-v22-bnb",
            "range-expansion-v2-bnb",
            "impulse-v1-run",
            "impulse-v1-research-variants",
            "mean-reversion-v1-run"
        };

        var resolved = new List<string>();
        foreach (var name in names)
        {
            var directory = Path.Combine(outputRoot, name);
            var zipPath = directory + ".zip";
            if (Directory.Exists(directory))
                resolved.Add(directory);
            else if (File.Exists(zipPath))
                resolved.Add(zipPath);
        }

        return resolved;
    }

    private static string? ResolveZipPath(string inputPath)
    {
        if (File.Exists(inputPath) && inputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return inputPath;

        var siblingZip = inputPath + ".zip";
        return File.Exists(siblingZip) ? siblingZip : null;
    }

    private static void ImportFromDirectory(
        string directory,
        bool includeBlockedCandidates,
        int blockedCandidateCap,
        List<MetaStrategyResearchRecord> records,
        HashSet<string> seen,
        List<MetaStrategyResearchImportSourceReport> sources)
    {
        var family = DetectFamily(directory);
        if (family is null)
        {
            sources.Add(new MetaStrategyResearchImportSourceReport(
                directory, "Unknown", string.Empty, 0, 0, "No recognized trade output files", "Directory"));
            return;
        }

        var tradeFiles = ResolveTradeFiles(directory, family);
        var executedImported = 0;
        foreach (var tradeFile in tradeFiles)
        {
            executedImported += ImportTradeFile(tradeFile, family, directory, records, seen);
        }

        var blockedImported = 0;
        var skippedBlockedReason = ImportBlockedFromDirectory(
            directory, family, includeBlockedCandidates, blockedCandidateCap, records, seen, ref blockedImported);

        sources.Add(new MetaStrategyResearchImportSourceReport(
            directory,
            family,
            tradeFiles.FirstOrDefault() ?? string.Empty,
            executedImported,
            blockedImported,
            skippedBlockedReason,
            "Directory"));
    }

    private static void ImportFromZip(
        string zipPath,
        bool includeBlockedCandidates,
        int blockedCandidateCap,
        List<MetaStrategyResearchRecord> records,
        HashSet<string> seen,
        List<MetaStrategyResearchImportSourceReport> sources)
    {
        var family = DetectFamilyFromZipPath(zipPath);
        using var archive = ZipFile.OpenRead(zipPath);
        var tradeEntries = ResolveZipTradeEntries(archive, family);
        if (tradeEntries.Count == 0)
        {
            family ??= DetectFamilyFromZipEntries(archive);
            tradeEntries = family is null ? [] : ResolveZipTradeEntries(archive, family);
        }

        if (family is null || tradeEntries.Count == 0)
        {
            sources.Add(new MetaStrategyResearchImportSourceReport(
                zipPath, family ?? "Unknown", string.Empty, 0, 0, "No recognized trade JSON entries in zip", "ZipArchive"));
            return;
        }

        var executedImported = 0;
        foreach (var entry in tradeEntries)
        {
            using var stream = entry.Open();
            executedImported += ImportTradeStream(stream, family, zipPath, records, seen);
        }

        var blockedImported = 0;
        string? skippedBlockedReason = "Blocked import disabled";
        if (includeBlockedCandidates)
        {
            var blockedEntry = ResolveZipBlockedEntry(archive, family);
            if (blockedEntry is null)
            {
                skippedBlockedReason = "Blocked file not found in zip";
            }
            else if (blockedEntry.Length > 50 * 1024 * 1024)
            {
                skippedBlockedReason = $"Blocked zip entry too large ({blockedEntry.Length / (1024 * 1024)} MB); cap={blockedCandidateCap}";
                using var stream = blockedEntry.Open();
                blockedImported = ImportBlockedStream(stream, family, zipPath, records, seen, blockedCandidateCap);
            }
            else
            {
                using var stream = blockedEntry.Open();
                blockedImported = ImportBlockedStream(stream, family, zipPath, records, seen, blockedCandidateCap);
                skippedBlockedReason = null;
            }
        }

        sources.Add(new MetaStrategyResearchImportSourceReport(
            zipPath,
            family,
            tradeEntries[0].FullName,
            executedImported,
            blockedImported,
            skippedBlockedReason,
            "ZipArchive"));
    }

    private static string? ImportBlockedFromDirectory(
        string directory,
        string family,
        bool includeBlockedCandidates,
        int blockedCandidateCap,
        List<MetaStrategyResearchRecord> records,
        HashSet<string> seen,
        ref int blockedImported)
    {
        if (!includeBlockedCandidates)
            return "Blocked import disabled";

        var blockedFile = ResolveBlockedFile(directory, family);
        if (blockedFile is null)
            return "Blocked file not found";

        var fileInfo = new FileInfo(blockedFile);
        if (fileInfo.Length > 50 * 1024 * 1024)
        {
            blockedImported = ImportBlockedFile(blockedFile, family, directory, records, seen, blockedCandidateCap);
            return $"Blocked file too large ({fileInfo.Length / (1024 * 1024)} MB); cap={blockedCandidateCap}";
        }

        blockedImported = ImportBlockedFile(blockedFile, family, directory, records, seen, blockedCandidateCap);
        return null;
    }

    private static string? DetectFamily(string directory)
    {
        if (File.Exists(Path.Combine(directory, "impulse-continuation-trades.json"))
            || Directory.GetFiles(directory, "impulse-continuation-trades.json", SearchOption.AllDirectories).Length > 0)
            return "ImpulseContinuationV1";

        if (File.Exists(Path.Combine(directory, "mean-reversion-range-trades.json"))
            || Directory.GetFiles(directory, "mean-reversion-range-trades.json", SearchOption.AllDirectories).Length > 0)
            return "MeanReversionRangeBounceV1";

        if (File.Exists(Path.Combine(directory, "range-expansion-v2-trades.json")))
        {
            var dirName = Path.GetFileName(directory);
            if (dirName.Contains("v23", StringComparison.OrdinalIgnoreCase))
                return "RangeExpansionV23";
            if (dirName.Contains("v24", StringComparison.OrdinalIgnoreCase))
                return "RangeExpansionV24";
            if (dirName.Contains("feasibility", StringComparison.OrdinalIgnoreCase))
                return "RangeExpansionV2Feasibility";
            return "RangeExpansionV2";
        }

        if (Directory.GetFiles(directory, "impulse-continuation-trades.json", SearchOption.AllDirectories).Length > 0)
            return "ImpulseContinuationV1";
        if (Directory.GetFiles(directory, "mean-reversion-range-trades.json", SearchOption.AllDirectories).Length > 0)
            return "MeanReversionRangeBounceV1";
        if (Directory.GetFiles(directory, "range-expansion-v2-trades.json", SearchOption.AllDirectories).Length > 0)
            return "RangeExpansionV2";

        return null;
    }

    private static IReadOnlyList<string> ResolveTradeFiles(string directory, string family)
    {
        var rootCandidates = family switch
        {
            "ImpulseContinuationV1" => new[] { "impulse-continuation-trades.json", "trades.json" },
            "MeanReversionRangeBounceV1" => new[] { "mean-reversion-range-trades.json", "trades.json" },
            _ => new[] { "range-expansion-v2-trades.json", "trades.json" }
        };

        foreach (var name in rootCandidates)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path) && new FileInfo(path).Length > 10)
                return [path];
        }

        var windowFiles = new List<string>();
        foreach (var window in new[] { "30d", "60d", "90d" })
        {
            var windowDir = Path.Combine(directory, window);
            if (!Directory.Exists(windowDir))
                continue;

            foreach (var name in rootCandidates)
            {
                var path = Path.Combine(windowDir, name);
                if (File.Exists(path) && new FileInfo(path).Length > 10)
                {
                    windowFiles.Add(path);
                    break;
                }
            }
        }

        return windowFiles;
    }

    private static string? ResolveBlockedFile(string directory, string family)
    {
        var name = family switch
        {
            "ImpulseContinuationV1" => "impulse-continuation-blocked-candidates.json",
            "MeanReversionRangeBounceV1" => "mean-reversion-range-blocked-candidates.json",
            _ => "range-expansion-v2-blocked-candidates.json"
        };

        var path = Path.Combine(directory, name);
        return File.Exists(path) ? path : null;
    }

    private static int ImportTradeFile(
        string path,
        string family,
        string sourceDirectory,
        List<MetaStrategyResearchRecord> destination,
        HashSet<string> seen)
    {
        using var stream = File.OpenRead(path);
        return ImportTradeStream(stream, family, sourceDirectory, destination, seen);
    }

    private static int ImportTradeStream(
        Stream stream,
        string family,
        string sourceDirectory,
        List<MetaStrategyResearchRecord> destination,
        HashSet<string> seen)
    {
        return family switch
        {
            "ImpulseContinuationV1" => ImportJsonArray<ImpulseContinuationV1CandidateRecord>(
                stream, r => MapImpulse(r, family, sourceDirectory), destination, seen, executedOnly: true),
            "MeanReversionRangeBounceV1" => ImportJsonArray<MeanReversionRangeBounceV1CandidateRecord>(
                stream, r => MapMeanReversion(r, family, sourceDirectory), destination, seen, executedOnly: true),
            _ => ImportJsonArray<RangeExpansionV2CandidateRecord>(
                stream, r => MapRangeExpansion(r, family, sourceDirectory), destination, seen, executedOnly: true)
        };
    }

    private static int ImportBlockedStream(
        Stream stream,
        string family,
        string sourceDirectory,
        List<MetaStrategyResearchRecord> destination,
        HashSet<string> seen,
        int cap)
    {
        return family switch
        {
            "ImpulseContinuationV1" => ImportJsonArray<ImpulseContinuationV1CandidateRecord>(
                stream, r => MapImpulse(r, family, sourceDirectory), destination, seen, executedOnly: false, cap: cap),
            "MeanReversionRangeBounceV1" => ImportJsonArray<MeanReversionRangeBounceV1CandidateRecord>(
                stream, r => MapMeanReversion(r, family, sourceDirectory), destination, seen, executedOnly: false, cap: cap),
            _ => ImportJsonArray<RangeExpansionV2CandidateRecord>(
                stream, r => MapRangeExpansion(r, family, sourceDirectory), destination, seen, executedOnly: false, cap: cap)
        };
    }

    private static int ImportBlockedFile(
        string path,
        string family,
        string sourceDirectory,
        List<MetaStrategyResearchRecord> destination,
        HashSet<string> seen,
        int cap)
    {
        using var stream = File.OpenRead(path);
        return ImportBlockedStream(stream, family, sourceDirectory, destination, seen, cap);
    }

    private static string? DetectFamilyFromZipPath(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        if (name.Contains("impulse", StringComparison.OrdinalIgnoreCase))
            return "ImpulseContinuationV1";
        if (name.Contains("mean-reversion", StringComparison.OrdinalIgnoreCase))
            return "MeanReversionRangeBounceV1";
        if (name.Contains("v23", StringComparison.OrdinalIgnoreCase))
            return "RangeExpansionV23";
        if (name.Contains("v24", StringComparison.OrdinalIgnoreCase))
            return "RangeExpansionV24";
        if (name.Contains("feasibility", StringComparison.OrdinalIgnoreCase))
            return "RangeExpansionV2Feasibility";
        if (name.Contains("range-expansion", StringComparison.OrdinalIgnoreCase))
            return "RangeExpansionV2";
        return null;
    }

    private static string? DetectFamilyFromZipEntries(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Contains("impulse-continuation-trades.json", StringComparison.OrdinalIgnoreCase))
                return "ImpulseContinuationV1";
            if (entry.FullName.Contains("mean-reversion-range-trades.json", StringComparison.OrdinalIgnoreCase))
                return "MeanReversionRangeBounceV1";
            if (entry.FullName.Contains("range-expansion-v2-trades.json", StringComparison.OrdinalIgnoreCase))
                return "RangeExpansionV2";
        }

        return null;
    }

    private static List<ZipArchiveEntry> ResolveZipTradeEntries(ZipArchive archive, string? family)
    {
        var candidates = family switch
        {
            "ImpulseContinuationV1" => new[] { "impulse-continuation-trades.json", "trades.json" },
            "MeanReversionRangeBounceV1" => new[] { "mean-reversion-range-trades.json", "trades.json" },
            _ => new[] { "range-expansion-v2-trades.json", "trades.json" }
        };

        foreach (var name in candidates)
        {
            var rootEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(Path.GetFileName(e.FullName), name, StringComparison.OrdinalIgnoreCase)
                && e.Length > 10);
            if (rootEntry is not null)
                return [rootEntry];
        }

        var windowEntries = new List<ZipArchiveEntry>();
        foreach (var window in new[] { "30d", "60d", "90d" })
        {
            foreach (var name in candidates)
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Contains($"{window}/", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileName(e.FullName), name, StringComparison.OrdinalIgnoreCase)
                    && e.Length > 10);
                if (entry is not null)
                {
                    windowEntries.Add(entry);
                    break;
                }
            }
        }

        return windowEntries;
    }

    private static ZipArchiveEntry? ResolveZipBlockedEntry(ZipArchive archive, string family)
    {
        var name = family switch
        {
            "ImpulseContinuationV1" => "impulse-continuation-blocked-candidates.json",
            "MeanReversionRangeBounceV1" => "mean-reversion-range-blocked-candidates.json",
            _ => "range-expansion-v2-blocked-candidates.json"
        };

        return archive.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFileName(e.FullName), name, StringComparison.OrdinalIgnoreCase));
    }

    private static int ImportJsonArray<T>(
        Stream stream,
        Func<T, MetaStrategyResearchRecord> map,
        List<MetaStrategyResearchRecord> destination,
        HashSet<string> seen,
        bool executedOnly,
        int? cap = null)
    {
        var rows = JsonSerializer.Deserialize<List<T>>(stream, JsonOptions) ?? [];
        var imported = 0;

        foreach (var row in rows)
        {
            if (cap.HasValue && imported >= cap.Value)
                break;

            var record = map(row);
            if (executedOnly && !record.CandidateWasExecuted)
                continue;

            if (!executedOnly && record.CandidateWasExecuted)
                continue;

            var key = BuildDedupKey(record);
            if (!seen.Add(key))
                continue;

            destination.Add(record);
            imported++;
        }

        return imported;
    }

    private static string BuildDedupKey(MetaStrategyResearchRecord record)
        => string.Join("|",
            record.StrategyFamily,
            record.ProfileName,
            record.Symbol,
            record.Interval,
            record.WindowLabel,
            record.TimeUtc.ToString("O"),
            record.CandidateWasExecuted);

    private static MetaStrategyResearchRecord MapImpulse(
        ImpulseContinuationV1CandidateRecord r,
        string family,
        string sourceDirectory)
    {
        return new MetaStrategyResearchRecord
        {
            StrategyFamily = family,
            ProfileName = r.ProfileName,
            Symbol = ResolveSymbol(r.Symbols, r.Symbol),
            Interval = r.Interval,
            WindowLabel = r.WindowLabel,
            TimeUtc = r.TimeUtc,
            EntryPrice = r.EntryPrice,
            ExitReason = r.ExitReason,
            GrossPnlQuote = r.GrossPnlQuote,
            NetPnlQuote = r.NetPnlQuote,
            IsNetWinner = r.NetPnlQuote > 0m ? true : r.NetPnlQuote < 0m ? false : null,
            CandidateWasExecuted = r.Executed,
            RejectionReason = r.RejectionReason,
            ExpectedMovePercent = r.ExpectedMovePercent,
            RequiredGrossMovePercent = r.RequiredGrossMovePercent,
            StopDistancePercent = r.StopDistancePercent,
            RewardRisk = r.StopToLockRatio,
            MfePercent = r.MfePercent,
            MaePercent = r.MaePercent,
            ForwardMfe15Percent = r.ForwardMfe15Percent,
            ForwardMfe30Percent = r.ForwardMfe30Percent,
            ForwardMfe60Percent = r.ForwardMfe60Percent,
            ForwardMae15Percent = r.ForwardMae15Percent,
            ForwardMae30Percent = r.ForwardMae30Percent,
            ForwardMae60Percent = r.ForwardMae60Percent,
            TimeToTargetMinutes = r.TimeToLock90Minutes,
            DurationMinutes = r.DurationMinutes,
            BreakoutBodyStrengthPercent = r.ImpulseBodyStrengthPercent,
            VolumeExpansionRatio = r.VolumeExpansionRatio,
            StopToLockRatio = r.StopToLockRatio,
            TargetModelName = r.TargetModelName,
            ExitPolicyName = ResolveExitPolicyName(r.ProfileName),
            SourceDirectory = sourceDirectory
        };
    }

    private static MetaStrategyResearchRecord MapMeanReversion(
        MeanReversionRangeBounceV1CandidateRecord r,
        string family,
        string sourceDirectory)
    {
        return new MetaStrategyResearchRecord
        {
            StrategyFamily = family,
            ProfileName = r.ProfileName,
            Symbol = ResolveSymbol(r.Symbols, r.Symbol),
            Interval = r.Interval,
            WindowLabel = r.WindowLabel,
            TimeUtc = r.TimeUtc,
            EntryPrice = r.EntryPrice,
            ExitReason = r.ExitReason,
            GrossPnlQuote = r.GrossPnlQuote,
            NetPnlQuote = r.NetPnlQuote,
            IsNetWinner = r.IsWinner ?? (r.NetPnlQuote > 0m ? true : r.NetPnlQuote < 0m ? false : null),
            CandidateWasExecuted = r.Executed,
            RejectionReason = r.RejectionReason,
            ExpectedMovePercent = r.ExpectedMovePercent,
            RequiredGrossMovePercent = r.RequiredGrossMovePercent,
            StopDistancePercent = r.StopDistancePercent,
            RewardRisk = r.RewardRisk,
            MfePercent = r.MfePercent,
            MaePercent = r.MaePercent,
            ForwardMfe15Percent = r.ForwardMfe15Percent,
            ForwardMfe30Percent = r.ForwardMfe30Percent,
            ForwardMfe60Percent = r.ForwardMfe60Percent,
            ForwardMae15Percent = r.ForwardMae15Percent,
            ForwardMae30Percent = r.ForwardMae30Percent,
            ForwardMae60Percent = r.ForwardMae60Percent,
            TimeToTargetMinutes = r.TimeToTargetMinutes,
            DurationMinutes = r.DurationMinutes,
            ShortMaSlopePercent = r.TrendSlopePercent,
            RangeWidthPercent = r.RangeWidthPercent,
            TargetModelName = r.TargetModelName,
            ExitPolicyName = ResolveExitPolicyName(r.ProfileName),
            SourceDirectory = sourceDirectory
        };
    }

    private static MetaStrategyResearchRecord MapRangeExpansion(
        RangeExpansionV2CandidateRecord r,
        string family,
        string sourceDirectory)
    {
        return new MetaStrategyResearchRecord
        {
            StrategyFamily = family,
            ProfileName = r.ProfileName,
            Symbol = ResolveSymbol(r.Symbols, r.Symbol),
            Interval = r.Interval,
            WindowLabel = r.WindowLabel,
            TimeUtc = r.TimeUtc,
            EntryPrice = r.EntryPrice,
            ExitReason = r.ExitReason,
            GrossPnlQuote = r.GrossPnlQuote,
            NetPnlQuote = r.NetPnlQuote,
            IsNetWinner = r.IsWinner ?? (r.NetPnlQuote > 0m ? true : r.NetPnlQuote < 0m ? false : null),
            CandidateWasExecuted = r.Executed,
            RejectionReason = r.RejectionReason,
            ExpectedMovePercent = r.ExpectedMovePercent,
            RequiredGrossMovePercent = r.RequiredGrossMovePercent,
            StopDistancePercent = r.StructuralStopDistancePercent,
            MfePercent = r.MfePercent,
            MaePercent = r.MaePercent,
            ForwardMfe15Percent = r.ForwardMfe15Percent,
            ForwardMfe30Percent = r.ForwardMfe30Percent,
            ForwardMfe60Percent = r.ForwardMfe60Percent,
            ForwardMae15Percent = r.ForwardMae15Percent,
            ForwardMae30Percent = r.ForwardMae30Percent,
            ForwardMae60Percent = r.ForwardMae60Percent,
            TimeToTargetMinutes = r.TimeToLock90Minutes,
            DurationMinutes = r.DurationMinutes,
            RangeWidthPercent = r.RangeWidthPercent,
            BreakoutBodyStrengthPercent = r.BreakoutBodyStrengthPercent,
            VolumeExpansionRatio = r.VolumeExpansionRatio,
            AtrExpansionRatio = r.AtrExpansionRatio,
            StopToLockRatio = r.StopToLockRatio,
            TargetModelName = r.TargetModelName,
            ExitPolicyName = ResolveExitPolicyName(r.ProfileName),
            SourceDirectory = sourceDirectory
        };
    }

    private static string ResolveSymbol(string symbols, TradingSymbol symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbols))
            return symbols.Trim();

        return symbol.ToString();
    }

    public static string ResolveExitPolicyName(string profileName)
    {
        if (profileName.Contains("profit-target", StringComparison.OrdinalIgnoreCase)
            || profileName.Contains("profittarget", StringComparison.OrdinalIgnoreCase))
            return "ProfitTarget";

        if (profileName.Contains("lock90", StringComparison.OrdinalIgnoreCase))
            return "Lock90";

        if (profileName.Contains("lock95", StringComparison.OrdinalIgnoreCase))
            return "Lock95";

        if (profileName.Contains("hold", StringComparison.OrdinalIgnoreCase))
            return "TimeStopVariant";

        if (profileName.Contains("midpoint", StringComparison.OrdinalIgnoreCase))
            return "MidpointTarget";

        return "Unknown";
    }
}
