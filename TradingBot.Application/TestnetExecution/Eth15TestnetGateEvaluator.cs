using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TradingBot.Application.TestnetExecution;

/// <summary>
/// Reads the ETH15 fixed-frequency forward-incubation research outputs (read-only) and decides
/// whether the frozen candidate currently satisfies every gate required to place a testnet
/// order: activation passed, an exact entry signal is present, the candidate is not Parked, the
/// forward stress-plus net is positive, and all frozen-profile health gates pass. It never
/// modifies frozen profiles, thresholds, or strategy logic; it only consumes their outputs.
/// </summary>
public sealed class Eth15TestnetGateEvaluator(ILogger<Eth15TestnetGateEvaluator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Eth15GateDecision Evaluate(string incubationOutputDirectory)
    {
        var summaryPath = Path.Combine(incubationOutputDirectory, "forward-incubation-summary.json");
        var gatesPath = Path.Combine(incubationOutputDirectory, "forward-incubation-health-gates.json");
        var profilePath = Path.Combine(incubationOutputDirectory, "frozen-profile.json");

        if (!File.Exists(summaryPath))
        {
            return Blocked($"Incubation summary not found at '{summaryPath}'. Run the backtest incubation first.");
        }

        Eth15IncubationSnapshot summary;
        Eth15FrozenProfileInfo? profile = null;
        var gates = new List<Eth15HealthGate>();

        try
        {
            summary = ParseSummary(File.ReadAllText(summaryPath));

            if (File.Exists(gatesPath))
                gates = ParseGates(File.ReadAllText(gatesPath));

            if (File.Exists(profilePath))
                profile = ParseProfile(File.ReadAllText(profilePath));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse ETH15 incubation outputs at {Directory}", incubationOutputDirectory);
            return Blocked($"Failed to parse incubation outputs: {ex.Message}");
        }

        var activationPassed = summary.ActivatedCheckpointCount > 0;
        var entryPresent = summary.CurrentExactEntryPresent;
        var parked = string.Equals(summary.Verdict, "Park", StringComparison.OrdinalIgnoreCase);
        var stressPlusPositive = summary.NetStressPlus > 0m;
        var applicableGates = gates.Where(g => g.Applicable).ToList();
        var allGatesPass = applicableGates.Count > 0 && applicableGates.All(g => g.Pass);
        var testnetCandidate = summary.TestnetOrderCandidate;

        var canPlace = testnetCandidate
                       && activationPassed
                       && entryPresent
                       && !parked
                       && stressPlusPositive
                       && allGatesPass;

        var blockedReason = canPlace
            ? string.Empty
            : BuildBlockedReason(summary, activationPassed, entryPresent, parked, stressPlusPositive, testnetCandidate, applicableGates);

        return new Eth15GateDecision
        {
            CanPlaceOrder = canPlace,
            ActivationPassed = activationPassed,
            EntryPresent = entryPresent,
            Parked = parked,
            StressPlusPositive = stressPlusPositive,
            AllHealthGatesPass = allGatesPass,
            TestnetOrderCandidate = testnetCandidate,
            Verdict = summary.Verdict,
            NetStressPlus = summary.NetStressPlus,
            BlockedReason = blockedReason,
            HealthGates = gates,
            Profile = profile,
            Summary = summary
        };
    }

    private static string BuildBlockedReason(
        Eth15IncubationSnapshot summary,
        bool activationPassed,
        bool entryPresent,
        bool parked,
        bool stressPlusPositive,
        bool testnetCandidate,
        IReadOnlyList<Eth15HealthGate> applicableGates)
    {
        var reasons = new List<string>();
        if (!testnetCandidate)
            reasons.Add($"VerdictNotTestnetOrderCandidate(Verdict={summary.Verdict})");
        if (parked)
            reasons.Add("CandidateParked");
        if (!activationPassed)
            reasons.Add($"ActivationNotPassed(ActivatedCheckpoints={summary.ActivatedCheckpointCount})");
        if (!entryPresent)
            reasons.Add("NoCurrentExactEntrySignal");
        if (!stressPlusPositive)
            reasons.Add($"StressPlusNetNotPositive(NetStressPlus={summary.NetStressPlus.ToString(CultureInfo.InvariantCulture)})");

        var failedGates = applicableGates.Where(g => !g.Pass).Select(g => g.GateName).ToList();
        if (failedGates.Count > 0)
            reasons.Add($"FailedHealthGates=[{string.Join(",", failedGates)}]");

        return reasons.Count > 0
            ? string.Join("; ", reasons)
            : "BlockedForUnknownReason";
    }

    private static Eth15IncubationSnapshot ParseSummary(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return new Eth15IncubationSnapshot
        {
            Verdict = GetString(r, "Verdict") ?? string.Empty,
            NetModerate = GetDecimal(r, "NetModerate"),
            NetStressPlus = GetDecimal(r, "NetStressPlus"),
            ForwardTrades = (int)GetLong(r, "ForwardTrades"),
            ActivatedCheckpointCount = (int)GetLong(r, "ActivatedCheckpointCount"),
            ActivationCheckpointCount = (int)GetLong(r, "ActivationCheckpointCount"),
            CurrentExactEntryPresent = GetBool(r, "CurrentExactEntryPresent"),
            TestnetOrderCandidate = GetBool(r, "TestnetOrderCandidate"),
            LatestStatus = GetString(r, "LatestStatus") ?? string.Empty,
            ForwardWindowEndUtc = GetString(r, "ForwardWindowEndUtc") ?? string.Empty
        };
    }

    private List<Eth15HealthGate> ParseGates(string json)
    {
        var result = new List<Eth15HealthGate>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            result.Add(new Eth15HealthGate
            {
                GateName = GetString(el, "GateName") ?? string.Empty,
                Requirement = GetString(el, "Requirement") ?? string.Empty,
                ObservedValue = GetString(el, "ObservedValue") ?? string.Empty,
                Applicable = GetBool(el, "Applicable"),
                Pass = GetBool(el, "Pass"),
                Notes = GetString(el, "Notes") ?? string.Empty
            });
        }

        return result;
    }

    private static Eth15FrozenProfileInfo ParseProfile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var maxHold = (int)GetLong(r, "MaxHoldMinutes");
        if (maxHold == 0)
        {
            var holdHours = GetDecimal(r, "HoldHours");
            maxHold = holdHours > 0 ? (int)(holdHours * 60) : 240;
        }

        return new Eth15FrozenProfileInfo
        {
            ProfileName = GetString(r, "ProfileName") ?? string.Empty,
            Symbol = GetString(r, "Symbol") ?? "ETHUSDT",
            Interval = GetString(r, "Interval") ?? "15m",
            Direction = GetString(r, "Direction") ?? "Short",
            TargetPercent = GetDecimal(r, "TargetPercent"),
            StopPercent = GetDecimal(r, "StopPercent"),
            MaxHoldMinutes = maxHold
        };
    }

    private static Eth15GateDecision Blocked(string reason) => new()
    {
        CanPlaceOrder = false,
        BlockedReason = reason
    };

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));

    private static long GetLong(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
            return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : (long)v.GetDouble(),
            JsonValueKind.String => long.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 0,
            _ => 0
        };
    }

    private static decimal GetDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
            return 0m;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 0m,
            _ => 0m
        };
    }
}
