namespace TradingBot.Application.TestnetExecution;

/// <summary>Frozen-profile geometry read from the incubation outputs (read-only; never modified).</summary>
public sealed class Eth15FrozenProfileInfo
{
    public string ProfileName { get; init; } = string.Empty;
    public string Symbol { get; init; } = "ETHUSDT";
    public string Interval { get; init; } = "15m";
    public string Direction { get; init; } = "Short";
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
}

/// <summary>One frozen forward-incubation health gate, as produced by the research engine.</summary>
public sealed class Eth15HealthGate
{
    public string GateName { get; init; } = string.Empty;
    public string Requirement { get; init; } = string.Empty;
    public string ObservedValue { get; init; } = string.Empty;
    public bool Applicable { get; init; }
    public bool Pass { get; init; }
    public string Notes { get; init; } = string.Empty;
}

/// <summary>Snapshot of the incubation summary relevant to the testnet gate decision.</summary>
public sealed class Eth15IncubationSnapshot
{
    public string Verdict { get; init; } = string.Empty;
    public decimal NetModerate { get; init; }
    public decimal NetStressPlus { get; init; }
    public int ForwardTrades { get; init; }
    public int ActivatedCheckpointCount { get; init; }
    public int ActivationCheckpointCount { get; init; }
    public bool CurrentExactEntryPresent { get; init; }
    public bool TestnetOrderCandidate { get; init; }
    public string LatestStatus { get; init; } = string.Empty;
    public string ForwardWindowEndUtc { get; init; } = string.Empty;
}

/// <summary>Outcome of the testnet entry gate evaluation for a single worker cycle.</summary>
public sealed class Eth15GateDecision
{
    public bool CanPlaceOrder { get; init; }
    public bool ActivationPassed { get; init; }
    public bool EntryPresent { get; init; }
    public bool Parked { get; init; }
    public bool StressPlusPositive { get; init; }
    public bool AllHealthGatesPass { get; init; }
    public bool TestnetOrderCandidate { get; init; }
    public string Verdict { get; init; } = string.Empty;
    public decimal NetStressPlus { get; init; }
    public string BlockedReason { get; init; } = string.Empty;
    public IReadOnlyList<Eth15HealthGate> HealthGates { get; init; } = Array.Empty<Eth15HealthGate>();
    public Eth15FrozenProfileInfo? Profile { get; init; }
    public Eth15IncubationSnapshot? Summary { get; init; }
}
