# Prints Binance USD-M Futures TESTNET balances and open positions.
# Reads testnet credentials from TradingBot/appsettings.json (Eth15TestnetExecution section).
$ErrorActionPreference = 'Stop'

# Strip // comment lines: appsettings.json uses JSONC, which ConvertFrom-Json (PS 5.1) rejects.
$raw = Get-Content (Join-Path $PSScriptRoot '..\TradingBot\appsettings.json') |
    Where-Object { $_.TrimStart() -notmatch '^//' } | Out-String
$settings = $raw | ConvertFrom-Json
$key = $settings.Eth15TestnetExecution.TestnetApiKey
$secret = $settings.Eth15TestnetExecution.TestnetSecretKey
$base = $settings.Eth15TestnetExecution.TestnetBaseUrl

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
