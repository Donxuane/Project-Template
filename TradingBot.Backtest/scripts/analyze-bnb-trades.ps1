$base = Join-Path $PSScriptRoot "..\output\pullback-profitlock-robustness-90d-local"
$windows = @('30d','60d','90d')
$intervals = @('1m','3m','5m')
$all = @()

foreach ($w in $windows) {
    foreach ($i in $intervals) {
        $path = Join-Path $base "$w\$i\trades.json"
        if (-not (Test-Path $path)) { continue }
        $trades = Get-Content $path -Raw | ConvertFrom-Json
        foreach ($t in $trades) {
            if ($t.Symbols -eq 'BNBUSDT' -and $t.ProfileName -like '*-BNB') {
                $all += [PSCustomObject]@{
                    Window = $w
                    Interval = $i
                    Profile = $t.ProfileName
                    EntryTime = $t.EntryTimeUtc
                    NetPnl = [decimal]$t.NetPnlQuote
                    ExitReason = $t.ExitReason
                    ResidualExpectedMove = $t.ResidualExpectedMovePercent
                    ResidualRR = $t.ResidualRewardRisk
                    DistInvalidation = $t.DistanceToInvalidationPercent
                    TrendStrength = $t.TrendStrengthPercent
                    ShortMaSlope = $t.ShortMaSlopePercent
                    VolRegime = $t.VolatilityRegime
                    MFE = $t.MfePercent
                    MAE = $t.MaePercent
                    ProfitLock90 = $t.ProfitCapture90Touched
                    ProfitLock95 = $t.ProfitCapture95Touched
                    ProfitLock98 = $t.ProfitCapture98Touched
                    ProfitLockThreshold = $t.ProfitLockThresholdPercent
                    ExitPolicy = $t.ExitPolicyName
                    RewardRisk = $t.RewardRisk
                    ExpectedMove = $t.ExpectedMovePercent
                }
            }
        }
    }
}

$outPath = Join-Path $base "bnb-only-trade-analysis.csv"
$all | Sort-Object Window, Interval, Profile, EntryTime | Export-Csv -Path $outPath -NoTypeInformation

Write-Host "Total BNB-only trades: $($all.Count)"
$winners = $all | Where-Object { $_.NetPnl -gt 0 }
$losers = $all | Where-Object { $_.NetPnl -le 0 }
Write-Host "Winners: $($winners.Count) Losers: $($losers.Count)"
Write-Host "CSV: $outPath"

$fields = @('ResidualExpectedMove','ResidualRR','DistInvalidation','TrendStrength','ShortMaSlope','MFE','MAE')
Write-Host "`n=== FIELD AVERAGES: WINNERS vs LOSERS (all windows) ==="
foreach ($f in $fields) {
    $wAvg = ($winners | Where-Object { $_.$f -ne $null } | Measure-Object -Property $f -Average).Average
    $lAvg = ($losers | Where-Object { $_.$f -ne $null } | Measure-Object -Property $f -Average).Average
    Write-Host "$f : winner=$wAvg loser=$lAvg"
}

Write-Host "`n=== EXIT REASON BREAKDOWN ==="
$all | Group-Object ExitReason | Sort-Object Count -Descending | ForEach-Object { Write-Host "$($_.Name): $($_.Count)" }

Write-Host "`n=== PROFIT LOCK TOUCH (losers only) ==="
Write-Host "90 touched: $(($losers | Where-Object ProfitLock90).Count)"
Write-Host "95 touched: $(($losers | Where-Object ProfitLock95).Count)"
Write-Host "98 touched: $(($losers | Where-Object ProfitLock98).Count)"

Write-Host "`n=== 60d/90d LOSERS DETAIL ==="
$losers | Where-Object { $_.Window -in @('60d','90d') } | Format-Table Window, Interval, Profile, EntryTime, NetPnl, ExitReason, ResidualExpectedMove, ResidualRR, DistInvalidation, TrendStrength, ShortMaSlope, VolRegime, MFE, MAE, ProfitLock90, ProfitLock95, ProfitLock98 -AutoSize

Write-Host "`n=== REFERENCE WINNER (30d 5m prevhigh-98, first positive) ==="
$ref = $all | Where-Object { $_.Window -eq '30d' -and $_.Interval -eq '5m' -and $_.Profile -eq 'pullback-prevhigh-profitlock-98-BNB' -and $_.NetPnl -gt 0 } | Select-Object -First 1
$ref | Format-List
