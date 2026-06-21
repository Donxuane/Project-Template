$csv = Import-Csv (Join-Path $PSScriptRoot "..\output\pullback-profitlock-robustness-90d-local\bnb-only-trade-analysis.csv")

# Deduplicate to unique setups (entry time + interval + variant family)
$unique = $csv | Group-Object { "$($_.EntryTime)|$($_.Interval)|$(if ($_.Profile -like 'pullback-v2*') {'v2'} else {'prevhigh'})" } |
    ForEach-Object { $_.Group | Select-Object -First 1 }

$winner = $unique | Where-Object { $_.NetPnl -gt 0 } | Select-Object -First 1
$losers = $unique | Where-Object { [decimal]$_.NetPnl -le 0 }

Write-Host "Unique setups: $($unique.Count) (winners: $(($unique | Where-Object NetPnl -gt 0).Count), losers: $($losers.Count))"
Write-Host "Reference winner: $($winner.Profile) $($winner.Window) $($winner.Interval) $($winner.EntryTime)"
Write-Host ""

$filters = @(
    @{ Name = 'ExpectedMove <= 0.50'; Test = { param($t) [decimal]$t.ExpectedMove -le 0.50 } },
    @{ Name = 'ExpectedMove <= 0.47'; Test = { param($t) [decimal]$t.ExpectedMove -le 0.47 } },
    @{ Name = 'DistInvalidation <= 0.40'; Test = { param($t) [decimal]$t.DistInvalidation -le 0.40 } },
    @{ Name = 'DistInvalidation <= 0.36'; Test = { param($t) [decimal]$t.DistInvalidation -le 0.36 } },
    @{ Name = 'TrendStrength <= 0.00090'; Test = { param($t) [decimal]$t.TrendStrength -le 0.00090 } },
    @{ Name = 'TrendStrength <= 0.00085'; Test = { param($t) [decimal]$t.TrendStrength -le 0.00085 } },
    @{ Name = 'ShortMaSlope <= 0.00055'; Test = { param($t) [decimal]$t.ShortMaSlope -le 0.00055 } },
    @{ Name = 'RewardRisk <= 1.35'; Test = { param($t) [decimal]$t.RewardRisk -le 1.35 } },
    @{ Name = 'ResidualExpectedMove <= 0.45 (v2)'; Test = { param($t) if ($t.Profile -notlike 'pullback-v2*') { return $true }; [decimal]$t.ResidualExpectedMove -le 0.45 } },
    @{ Name = 'ResidualRR <= 1.10 (v2)'; Test = { param($t) if ($t.Profile -notlike 'pullback-v2*') { return $true }; [decimal]$t.ResidualRR -le 1.10 } },
    @{ Name = 'DistInv<=0.40 AND Trend<=0.00090'; Test = { param($t) [decimal]$t.DistInvalidation -le 0.40 -and [decimal]$t.TrendStrength -le 0.00090 } },
    @{ Name = 'DistInv<=0.40 AND ExpMove<=0.50'; Test = { param($t) [decimal]$t.DistInvalidation -le 0.40 -and [decimal]$t.ExpectedMove -le 0.50 } },
    @{ Name = 'DistInv<=0.40 AND Trend<=0.00090 AND Slope<=0.00055'; Test = { param($t) [decimal]$t.DistInvalidation -le 0.40 -and [decimal]$t.TrendStrength -le 0.00090 -and [decimal]$t.ShortMaSlope -le 0.00055 } }
)

foreach ($f in $filters) {
    $wPass = & $f.Test $winner
    $lBlocked = ($losers | Where-Object { & $f.Test $_ }).Count
    $lPassed = ($losers | Where-Object { -not (& $f.Test $_) }).Count
    Write-Host "$($f.Name): winnerPass=$wPass losersBlocked=$lBlocked losersPass=$lPassed"
}

Write-Host "`n=== UNIQUE SETUP TABLE ==="
$unique | Select-Object Window, Interval, Profile, EntryTime, NetPnl, ExitReason, ExpectedMove, ResidualExpectedMove, RewardRisk, ResidualRR, DistInvalidation, TrendStrength, ShortMaSlope, VolRegime, MFE, MAE, ProfitLock98 | Format-Table -AutoSize
