import json
import os
from collections import Counter, defaultdict
from datetime import datetime, timezone

BASE = os.path.join(os.path.dirname(__file__), "..")
paths = [
    ("retest", os.path.join(BASE, "output/bnb-retest-robustness-90d-local/90d/blocked-entries.json")),
    ("guard5m", os.path.join(BASE, "output/bnb-guard-robustness-90d-local/90d/5m/blocked-entries.json")),
    ("guard1m", os.path.join(BASE, "output/bnb-guard-robustness-90d-local/90d/1m/blocked-entries.json")),
    ("guard3m", os.path.join(BASE, "output/bnb-guard-robustness-90d-local/90d/3m/blocked-entries.json")),
    ("baseline_blk", os.path.join(BASE, "output/pullback-profitlock-robustness-90d-local/90d/5m/blocked-entries.json")),
]
trade_paths = [
    ("90d", os.path.join(BASE, "output/pullback-profitlock-robustness-90d-local/90d/5m/trades.json")),
    ("60d", os.path.join(BASE, "output/pullback-profitlock-robustness-90d-local/60d/5m/trades.json")),
    ("30d", os.path.join(BASE, "output/pullback-profitlock-robustness-90d-local/30d/5m/trades.json")),
]

raw = json.load(open(os.path.join(BASE, "data/BNBUSDT-1m.json")))
candles = []
for row in raw:
    t = datetime.fromtimestamp(row[0] / 1000, tz=timezone.utc)
    candles.append({"t": t, "close": float(row[4]), "high": float(row[2])})


def forward_mfe(entry_iso: str, minutes: int = 60):
    entry = datetime.fromisoformat(entry_iso.replace("Z", "+00:00"))
    idx = max(i for i, c in enumerate(candles) if c["t"] <= entry)
    price = candles[idx]["close"]
    end = entry.timestamp() + minutes * 60
    maxh = price
    for c in candles[idx + 1 :]:
        if c["t"].timestamp() > end:
            break
        maxh = max(maxh, c["high"])
    return round((maxh - price) / price * 100, 4)


def lock_dist(em, pct=90):
    return round(float(em) * pct / 100, 4) if em else None


blocks = []
for src, p in paths:
    if not os.path.exists(p):
        continue
    for b in json.load(open(p)):
        if b.get("Symbols") != "BNBUSDT":
            continue
        blocks.append({**b, "source": src})

by_key = defaultdict(list)
for b in blocks:
    by_key[(b["Interval"], b["TimeUtc"])].append(b)

print("=== UNIQUE BNB BLOCKED (90d experiment outputs) ===")
print(f"Count: {len(by_key)}")
rows = []
for (interval, time), group in sorted(by_key.items()):
    em = group[0].get("RawExpectedMovePercent") or group[0].get("ExpectedMovePercent")
    reasons = sorted(set(g["Reason"] for g in group))
    layers = sorted(set(g.get("RejectionLayer", "") for g in group))
    mfe = forward_mfe(time)
    l90 = lock_dist(em, 90)
    timing = "unknown"
    if l90 is not None:
        if mfe >= l90:
            timing = "same-time MFE could reach PL90"
        elif mfe >= 0.20:
            timing = "some MFE, below inflated lock"
        elif mfe < 0.05:
            timing = "no follow-through"
        else:
            timing = "weak follow-through"
    rows.append(
        {
            "time": time,
            "interval": interval,
            "primary": Counter(g["Reason"] for g in group).most_common(1)[0][0],
            "em": em,
            "mfe60": mfe,
            "lock90": l90,
            "reasons": reasons,
            "layers": layers,
            "timing": timing,
            "consec": group[0].get("ConsecutiveBullishCandlesAtEntry"),
        }
    )
    print(f"{time} {interval:3s} EM={em} MFE60={mfe} lock90={l90} -> {timing}")
    print(f"  reasons: {' | '.join(reasons)}")
    print(f"  layers: {', '.join(layers)}")

print("\n=== GROUP BY PRIMARY REASON ===")
for reason, items in sorted(
    {r["primary"]: [x for x in rows if x["primary"] == r["primary"]] for r in rows}.items(),
    key=lambda kv: -len(kv[1]),
):
    avg = sum(x["mfe60"] for x in items) / len(items)
    reach = sum(1 for x in items if x["lock90"] and x["mfe60"] >= x["lock90"])
    print(f"\n[{reason}] n={len(items)} avgMFE60={avg:.3f}% PL90reachable={reach}/{len(items)}")
    for x in items:
        print(
            f"  {x['time']} {x['interval']} EM={x['em']} MFE60={x['mfe60']} lock90={x['lock90']} -> {x['timing']}"
        )

print("\n=== EXECUTED BNB TRADES (baseline, deduped by entry) ===")
seen = set()
for win, p in trade_paths:
    if not os.path.exists(p):
        continue
    for t in json.load(open(p)):
        if t.get("Symbols") != "BNBUSDT":
            continue
        k = t["EntryTimeUtc"]
        if k in seen:
            continue
        seen.add(k)
        em = t.get("ExpectedMovePercent")
        mfe = t.get("MfePercent")
        l90 = lock_dist(em, 90)
        outcome = "WINNER" if float(t["NetPnlQuote"]) > 0 else "LOSER"
        print(
            f"{k} window={win} {outcome} net={float(t['NetPnlQuote']):.5f} "
            f"MFE={float(mfe or 0):.3f} EM={float(em or 0):.3f} lock90={l90} "
            f"exit={t['ExitReason']} PL90={t.get('ProfitCapture90Touched')}"
        )

print("\n=== ALTERNATIVE ENTRY TIMING (forward MFE60, +0/5/10/15/30m) ===")
anchors = [
    ("Mar15 prevhigh loser", "2026-03-15T14:06:00Z", 0.738),
    ("Mar15 V2 staging fail", "2026-03-15T14:11:00Z", None),
    ("Apr08 prevhigh loser", "2026-04-08T15:31:00Z", 0.907),
    ("Apr08 V2 loser", "2026-04-08T15:36:00Z", 0.820),
    ("May13 prevhigh winner", "2026-05-13T02:41:00Z", 0.466),
    ("May13 V2 winner", "2026-05-13T02:46:00Z", 0.367),
]
for label, a, em in anchors:
    l90 = lock_dist(em, 90) if em else None
    print(f"\n{label} @ {a} lock90={l90}")
    base_t = datetime.fromisoformat(a.replace("Z", "+00:00"))
    for off in [0, 5, 10, 15, 30]:
        iso = datetime.fromtimestamp(base_t.timestamp() + off * 60, tz=timezone.utc).strftime(
            "%Y-%m-%dT%H:%M:%SZ"
        )
        mfe = forward_mfe(iso)
        flag = " *PL90 ok*" if l90 and mfe >= l90 else ""
        print(f"  +{off:2d}m ({iso}) MFE60={mfe}%{flag}")
