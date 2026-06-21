$ErrorActionPreference = "Stop"
$base = Join-Path $PSScriptRoot ".."
$paths = @(
    @{ Src = "retest"; Path = Join-Path $base "output\bnb-retest-robustness-90d-local\90d\blocked-entries.json" },
    @{ Src = "guard5m"; Path = Join-Path $base "output\bnb-guard-robustness-90d-local\90d\5m\blocked-entries.json" },
    @{ Src = "guard1m"; Path = Join-Path $base "output\bnb-guard-robustness-90d-local\90d\1m\blocked-entries.json" },
    @{ Src = "guard3m"; Path = Join-Path $base "output\bnb-guard-robustness-90d-local\90d\3m\blocked-entries.json" },
    @{ Src = "baseline_blk"; Path = Join-Path $base "output\pullback-profitlock-robustness-90d-local\90d\5m\blocked-entries.json" }
)
$tradePaths = @(
    @{ Win = "90d"; Path = Join-Path $base "output\pullback-profitlock-robustness-90d-local\90d\5m\trades.json" },
    @{ Win = "60d"; Path = Join-Path $base "output\pullback-profitlock-robustness-90d-local\60d\5m\trades.json" },
    @{ Win = "30d"; Path = Join-Path $base "output\pullback-profitlock-robustness-90d-local\30d\5m\trades.json" }
)

function ConvertFrom-RawCandles([object]$raw) {
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($row in $raw) {
        $openTime = [DateTimeOffset]::FromUnixTimeMilliseconds([int64]$row[0]).UtcDateTime
        $list.Add([pscustomobject]@{
            OpenTimeUtc = $openTime
            Close = [decimal]$row[4]
            High = [decimal]$row[2]
        })
    }
    return $list
}

function Get-ForwardMfePercent($candles, [string]$entryIso, [int]$minutes = 60) {
    $entryUtc = [datetime]::Parse($entryIso, $null, [Globalization.DateTimeStyles]::RoundtripKind)
    $entryIdx = -1
    for ($i = $candles.Count - 1; $i -ge 0; $i--) {
        if ($candles[$i].OpenTimeUtc -le $entryUtc) { $entryIdx = $i; break }
    }
    if ($entryIdx -lt 0) { return $null }
    $entryPrice = [decimal]$candles[$entryIdx].Close
    if ($entryPrice -le 0) { return $null }
    $end = $entryUtc.AddMinutes($minutes)
    $maxHigh = $entryPrice
    for ($i = $entryIdx + 1; $i -lt $candles.Count; $i++) {
        if ($candles[$i].OpenTimeUtc -gt $end) { break }
        if ([decimal]$candles[$i].High -gt $maxHigh) { $maxHigh = [decimal]$candles[$i].High }
    }
    return [math]::Round(($maxHigh - $entryPrice) / $entryPrice * 100, 4)
}

function Get-Lock90([decimal]$em) { return [math]::Round($em * 0.9, 4) }

function Get-TimingAssessment($mfe60, $lock90) {
    if ($null -eq $mfe60 -or $null -eq $lock90) { return "unknown" }
    if ($mfe60 -ge $lock90) { return "same-time MFE could reach PL90" }
    if ($mfe60 -ge 0.20) { return "some MFE, below inflated lock distance" }
    if ($mfe60 -lt 0.05) { return "no follow-through; avoid entry" }
    return "weak follow-through"
}

Write-Host "Loading candles..."
$candles = ConvertFrom-RawCandles (Get-Content (Join-Path $base "data\BNBUSDT-1m.json") -Raw | ConvertFrom-Json)

$blocks = @()
foreach ($p in $paths) {
    if (-not (Test-Path $p.Path)) { continue }
    $data = Get-Content $p.Path -Raw | ConvertFrom-Json
    foreach ($b in $data) {
        if ($b.Symbols -ne "BNBUSDT") { continue }
        $blocks += [pscustomobject]@{
            Source = $p.Src
            Interval = $b.Interval
            TimeUtc = [string]$b.TimeUtc
            Reason = [string]$b.Reason
            Layer = [string]$b.RejectionLayer
            EM = if ($null -ne $b.RawExpectedMovePercent) { [decimal]$b.RawExpectedMovePercent } elseif ($null -ne $b.ExpectedMovePercent) { [decimal]$b.ExpectedMovePercent } else { $null }
            Consec = $b.ConsecutiveBullishCandlesAtEntry
        }
    }
}

$rows = $blocks | Group-Object { "$($_.Interval)|$($_.TimeUtc)" } | ForEach-Object {
    $g = $_.Group
    $primary = ($g | Group-Object Reason | Sort-Object Count -Descending | Select-Object -First 1).Name
    $em = ($g | Where-Object { $null -ne $_.EM } | Select-Object -First 1).EM
    $mfe60 = Get-ForwardMfePercent $candles $g[0].TimeUtc 60
    $lock90 = if ($null -ne $em) { Get-Lock90 $em } else { $null }
    [pscustomobject]@{
        TimeUtc = $g[0].TimeUtc
        Interval = $g[0].Interval
        PrimaryReason = $primary
        AllReasons = (($g.Reason | Select-Object -Unique) -join " | ")
        Layers = (($g.Layer | Select-Object -Unique) -join ", ")
        EM = if ($null -ne $em) { [math]::Round($em, 3) } else { $null }
        Mfe60 = $mfe60
        Lock90 = $lock90
        Consec = ($g | Where-Object { $null -ne $_.Consec } | Select-Object -First 1).Consec
        Timing = Get-TimingAssessment $mfe60 $lock90
    }
} | Sort-Object TimeUtc

Write-Host "`n=== UNIQUE BNB BLOCKED SETUPS (90d outputs, all intervals) ==="
Write-Host "Count: $($rows.Count)"
$rows | Format-Table TimeUtc, Interval, PrimaryReason, EM, Mfe60, Lock90, Timing -AutoSize

Write-Host "`n=== GROUP BY PRIMARY REASON ==="
$rows | Group-Object PrimaryReason | Sort-Object Count -Descending | ForEach-Object {
    $avg = ($_.Group | Measure-Object -Property Mfe60 -Average).Average
    $reach = @($_.Group | Where-Object { $null -ne $_.Lock90 -and $_.Mfe60 -ge $_.Lock90 }).Count
    Write-Host "`n[$($_.Name)] n=$($_.Count) avgMFE60=$([math]::Round($avg,3))% PL90reachable=$reach/$($_.Count)"
    $_.Group | ForEach-Object {
        Write-Host "  $($_.TimeUtc) $($_.Interval) EM=$($_.EM)% MFE60=$($_.Mfe60)% lock90=$($_.Lock90)% -> $($_.Timing)"
    }
}

Write-Host "`n=== EXECUTED BNB TRADES (baseline, deduped) ==="
$seen = @{}
foreach ($tp in $tradePaths) {
    if (-not (Test-Path $tp.Path)) { continue }
    $trades = Get-Content $tp.Path -Raw | ConvertFrom-Json
    foreach ($t in $trades) {
        if ($t.Symbols -ne "BNBUSDT") { continue }
        $k = [string]$t.EntryTimeUtc
        if ($seen.ContainsKey($k)) { continue }
        $seen[$k] = $true
        $em = [decimal]$t.ExpectedMovePercent
        $mfe = [decimal]$t.MfePercent
        $lock90 = Get-Lock90 $em
        $outcome = if ([decimal]$t.NetPnlQuote -gt 0) { "WINNER" } else { "LOSER" }
        Write-Host "$k window=$($tp.Win) $outcome net=$([math]::Round([decimal]$t.NetPnlQuote,5)) MFE=$([math]::Round($mfe,3)) EM=$([math]::Round($em,3)) lock90=$lock90 exit=$($t.ExitReason) PL90=$($t.ProfitCapture90Touched)"
    }
}

Write-Host "`n=== ALTERNATIVE ENTRY TIMING (forward MFE60) ==="
$anchors = @(
    @{ Label = "Mar15 prevhigh loser"; Time = "2026-03-15T14:06:00Z"; EM = 0.738 },
    @{ Label = "Mar15 V2 staging fail"; Time = "2026-03-15T14:11:00Z"; EM = $null },
    @{ Label = "Apr08 prevhigh loser (60d)"; Time = "2026-04-08T15:31:00Z"; EM = 0.907 },
    @{ Label = "Apr08 V2 loser (60d)"; Time = "2026-04-08T15:36:00Z"; EM = 0.820 },
    @{ Label = "May13 prevhigh winner (30d)"; Time = "2026-05-13T02:41:00Z"; EM = 0.466 },
    @{ Label = "May13 V2 winner (30d)"; Time = "2026-05-13T02:46:00Z"; EM = 0.367 }
)
foreach ($a in $anchors) {
    $lock90 = if ($null -ne $a.EM) { Get-Lock90 $a.EM } else { $null }
    Write-Host "`n$($a.Label) @ $($a.Time) lock90=$lock90"
    $baseT = [datetime]::Parse($a.Time, $null, [Globalization.DateTimeStyles]::RoundtripKind)
    foreach ($off in @(0,5,10,15,30)) {
        $iso = $baseT.AddMinutes($off).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        $mfe = Get-ForwardMfePercent $candles $iso 60
        $flag = if ($null -ne $lock90 -and $null -ne $mfe -and $mfe -ge $lock90) { " *PL90 ok*" } else { "" }
        Write-Host "  +$off m ($iso) MFE60=$mfe%$flag"
    }
}
