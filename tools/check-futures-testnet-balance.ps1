# Prints Binance USD-M Futures TESTNET balances and open positions.
# Reads credentials from FuturesTestnet__ApiKey and FuturesTestnet__SecretKey.
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'futures-testnet-settings.ps1')
$settings = Get-FuturesTestnetSettings
$key = $settings.ApiKey
$secret = $settings.SecretKey
$base = $settings.BaseUrl

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [Text.Encoding]::UTF8.GetBytes($secret)

function Invoke-Signed([string]$path) {
    $serverTime = (Invoke-RestMethod ($base + '/fapi/v1/time')).serverTime
    $query = 'timestamp=' + $serverTime + '&recvWindow=60000'
    $sig = -join ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($query)) | ForEach-Object { $_.ToString('x2') })
    Invoke-RestMethod -Uri ($base + $path + '?' + $query + '&signature=' + $sig) -Headers @{ 'X-MBX-APIKEY' = $key }
}

Write-Host "=== Futures testnet balances (non-zero) ==="
$balances = @(Invoke-Signed '/fapi/v2/balance')
$balances | ForEach-Object { $_ } |
    Where-Object { [decimal]$_.balance -ne 0 -or [decimal]$_.crossUnPnl -ne 0 } |
    Format-Table asset, balance, crossWalletBalance, crossUnPnl, availableBalance -AutoSize

Write-Host "=== Open positions ==="
$positions = @(Invoke-Signed '/fapi/v2/positionRisk') | ForEach-Object { $_ } |
    Where-Object { [decimal]$_.positionAmt -ne 0 }
if ($positions) {
    $positions | Format-Table symbol, positionAmt, entryPrice, markPrice, unRealizedProfit, leverage -AutoSize
} else {
    Write-Host "(none)"
}
