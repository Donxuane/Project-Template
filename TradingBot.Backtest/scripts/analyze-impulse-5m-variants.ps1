param(
    [string]$OutputDir = "TradingBot.Backtest/output/impulse-v1-research-variants"
)

$summary = Get-Content (Join-Path $OutputDir "impulse-continuation-summary.json") -Raw | ConvertFrom-Json
$trades = Get-Content (Join-Path $OutputDir "impulse-continuation-trades.json") -Raw | ConvertFrom-Json
$robustness = Get-Content (Join-Path $OutputDir "impulse-continuation-window-robustness.json") -Raw | ConvertFrom-Json

$fiveM = $summary | Where-Object { $_.Interval -eq "5m" }
$fiveMTrades = $trades | Where-Object { $_.Interval -eq "5m" }

# Aggregate by profile across windows
$byProfile = $fiveM | Group-Object ProfileName | ForEach-Object {
    $rows = $_.Group
    $pnl = ($rows | Measure-Object -Property EstimatedNetPnlQuote -Sum).Sum
    $tradesCount = ($rows | Measure-Object -Property TradesCount -Sum).Sum
    $candidates = ($rows | Measure-Object -Property CandidateCount -Sum).Sum
    $avgMove = ($rows | Where-Object { $_.AvgExpectedMovePercent } | Measure-Object -Property AvgExpectedMovePercent -Average).Average
    [PSCustomObject]@{
        ProfileName = $_.Name
        TotalNetPnl = [decimal]$pnl
        TotalTrades = [int]$tradesCount
        NetPerTrade = if ($tradesCount -gt 0) { [decimal]$pnl / $tradesCount } else { 0 }
        TotalCandidates = [int]$candidates
        AvgExpectedMove = [decimal](if ($avgMove) { $avgMove } else { 0 })
        W30 = ($rows | Where-Object WindowLabel -eq "30d").EstimatedNetPnlQuote
        W60 = ($rows | Where-Object WindowLabel -eq "60d").EstimatedNetPnlQuote
        W90 = ($rows | Where-Object WindowLabel -eq "90d").EstimatedNetPnlQuote
    }
} | Sort-Object TotalNetPnl -Descending

Write-Host "=== 5m PROFILE RANKING BY NET PNL ==="
$byProfile | Select-Object -First 10 | Format-Table ProfileName, TotalNetPnl, TotalTrades, NetPerTrade, AvgExpectedMove -AutoSize

Write-Host "=== 5m PROFILE RANKING BY NET PER TRADE (min 30 trades) ==="
$byProfile | Where-Object { $_.TotalTrades -ge 30 } | Sort-Object NetPerTrade -Descending | Select-Object -First 10 | Format-Table ProfileName, TotalNetPnl, TotalTrades, NetPerTrade, AvgExpectedMove -AutoSize

function Get-ExitBreakdown($profileName) {
    $pt = $fiveMTrades | Where-Object { $_.ProfileName -eq $profileName }
    $pt | Group-Object ExitReason | ForEach-Object {
        [PSCustomObject]@{
            ExitReason = $_.Name
            Count = $_.Count
            NetPnl = ($_.Group | Measure-Object -Property NetPnlQuote -Sum).Sum
        }
    } | Sort-Object Count -Descending
}

$bestNet = $byProfile | Select-Object -First 1
$bestNpt = $byProfile | Where-Object { $_.TotalTrades -ge 30 } | Sort-Object NetPerTrade -Descending | Select-Object -First 1
$baseline = $byProfile | Where-Object { $_.ProfileName -match "-5m-lock90$" -and $_.ProfileName -notmatch "net|hold|move|expand|swing|midpoint" }

Write-Host "=== BASELINE 5m PROFILES (hybrid/net10/hold60/impulseLow) ==="
$baseline | Format-Table ProfileName, TotalNetPnl, TotalTrades, NetPerTrade, W30, W60, W90 -AutoSize

Write-Host "=== BEST BY NET: $($bestNet.ProfileName) ==="
Get-ExitBreakdown $bestNet.ProfileName | Format-Table -AutoSize

Write-Host "=== BEST BY NET/TRADE: $($bestNpt.ProfileName) ==="
Get-ExitBreakdown $bestNpt.ProfileName | Format-Table -AutoSize

# Variant family comparison
function Get-FamilyStats($pattern, $label) {
    $items = $byProfile | Where-Object { $_.ProfileName -match $pattern }
    if (-not $items) { return }
    $stopNet = 0m; $stopCount = 0
    foreach ($p in $items) {
        $bd = Get-ExitBreakdown $p.ProfileName
        $sl = $bd | Where-Object ExitReason -eq "StopLoss"
        if ($sl) { $stopNet += $sl.NetPnl; $stopCount += $sl.Count }
    }
    [PSCustomObject]@{
        Family = $label
        Profiles = $items.Count
        TotalNet = ($items | Measure-Object TotalNetPnl -Sum).Sum
        TotalTrades = ($items | Measure-Object TotalTrades -Sum).Sum
        AvgNetPerTrade = if (($items | Measure-Object TotalTrades -Sum).Sum -gt 0) {
            ($items | Measure-Object TotalNetPnl -Sum).Sum / ($items | Measure-Object TotalTrades -Sum).Sum
        } else { 0 }
        StopLossNet = $stopNet
        StopLossCount = $stopCount
        AvgExpectedMove = ($items | Measure-Object AvgExpectedMove -Average).Average
    }
}

Write-Host "=== VARIANT FAMILY COMPARISON (5m) ==="
@(
    Get-FamilyStats "-5m-lock90$" "Baseline (net10/hold60/hybrid/low)"
    Get-FamilyStats "-5m-net15-" "Net15"
    Get-FamilyStats "-5m-net20-" "Net20"
    Get-FamilyStats "-5m-hold30-" "Hold30"
    Get-FamilyStats "-5m-hold120-" "Hold120"
    Get-FamilyStats "-5m-impulse-move-" "ImpulseMoveTarget"
    Get-FamilyStats "-5m-atr-expand-" "AtrExpansionTarget"
    Get-FamilyStats "-5m-swing-target-" "RecentSwingTarget"
    Get-FamilyStats "-5m-midpoint-stop-" "MidpointStop"
) | Where-Object { $_ } | Format-Table Family, TotalNet, TotalTrades, AvgNetPerTrade, StopLossCount, StopLossNet, AvgExpectedMove -AutoSize

Write-Host "=== NEAR BREAKEVEN (sum 30+60+90 > -1.0, trades >= 30) ==="
$robust5m = $robustness | Where-Object { $_.Interval -eq "5m" }
$robust5m | Where-Object {
    ($_.Window30dNetPnl + $_.Window60dNetPnl + $_.Window90dNetPnl) -gt -1.0 -and
    ($_.Window30dTrades + $_.Window60dTrades + $_.Window90dTrades) -ge 30
} | Sort-Object { $_.Window30dNetPnl + $_.Window60dNetPnl + $_.Window90dNetPnl } -Descending |
    Format-Table ProfileName, Window30dNetPnl, Window60dNetPnl, Window90dNetPnl, RobustnessVerdict -AutoSize

Write-Host "=== WINDOW STABILITY (baseline vs best variant) ==="
$robust5m | Where-Object {
    $_.ProfileName -eq $bestNet.ProfileName -or
    ($_.ProfileName -match "-5m-lock90$" -and $_.ProfileName -notmatch "net|hold|move|expand|swing|midpoint")
} | Format-Table ProfileName, Window30dNetPnl, Window60dNetPnl, Window90dNetPnl, RobustnessVerdict -AutoSize
