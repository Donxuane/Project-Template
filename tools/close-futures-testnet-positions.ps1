# Closes all open Binance USD-M Futures TESTNET positions with reduce-only market orders.
$ErrorActionPreference = 'Stop'

$raw = Get-Content (Join-Path $PSScriptRoot '..\TradingBot\appsettings.json') |
    Where-Object { $_.TrimStart() -notmatch '^//' } | Out-String
$settings = $raw | ConvertFrom-Json
$key = $settings.Eth15TestnetExecution.TestnetApiKey
$secret = $settings.Eth15TestnetExecution.TestnetSecretKey
$base = $settings.Eth15TestnetExecution.TestnetBaseUrl

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [Text.Encoding]::UTF8.GetBytes($secret)

function Sign([string]$query) {
    -join ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($query)) | ForEach-Object { $_.ToString('x2') })
}

function SignedGet([string]$path) {
    $serverTime = (Invoke-RestMethod ($base + '/fapi/v1/time')).serverTime
    $query = 'timestamp=' + $serverTime + '&recvWindow=60000'
    $sig = Sign $query
    Invoke-RestMethod -Uri ($base + $path + '?' + $query + '&signature=' + $sig) -Headers @{ 'X-MBX-APIKEY' = $key }
}

function ClosePosition([string]$symbol, [string]$side, [string]$quantity) {
    $serverTime = (Invoke-RestMethod ($base + '/fapi/v1/time')).serverTime
    $query = 'symbol=' + $symbol +
             '&side=' + $side +
             '&type=MARKET' +
             '&quantity=' + $quantity +
             '&reduceOnly=true' +
             '&newOrderRespType=RESULT' +
             '&timestamp=' + $serverTime +
             '&recvWindow=60000'
    $sig = Sign $query
    Invoke-RestMethod -Method Post -Uri ($base + '/fapi/v1/order?' + $query + '&signature=' + $sig) -Headers @{ 'X-MBX-APIKEY' = $key }
}

$positions = @(SignedGet '/fapi/v2/positionRisk') | ForEach-Object { $_ } |
    Where-Object { [decimal]$_.positionAmt -ne 0 }

if (-not $positions) {
    Write-Host 'No open positions to close.'
    exit 0
}

foreach ($p in $positions) {
    $amt = [decimal]$p.positionAmt
    $qty = [Math]::Abs($amt).ToString([Globalization.CultureInfo]::InvariantCulture)
    $side = if ($amt -gt 0) { 'SELL' } else { 'BUY' }
    Write-Host ("Closing {0}: positionAmt={1} => {2} qty={3}" -f $p.symbol, $amt, $side, $qty)
    $result = ClosePosition $p.symbol $side $qty
    Write-Host ("  Closed. orderId={0} status={1} avgPrice={2} executedQty={3}" -f $result.orderId, $result.status, $result.avgPrice, $result.executedQty)
}

Write-Host ''
Write-Host '=== Remaining open positions ==='
$remaining = @(SignedGet '/fapi/v2/positionRisk') | ForEach-Object { $_ } |
    Where-Object { [decimal]$_.positionAmt -ne 0 }
if ($remaining) {
    $remaining | Format-Table symbol, positionAmt, entryPrice, markPrice, unRealizedProfit, leverage -AutoSize
} else {
    Write-Host '(none)'
}

Write-Host ''
Write-Host '=== Wallet after close ==='
@(SignedGet '/fapi/v2/balance') | ForEach-Object { $_ } |
    Where-Object { [decimal]$_.balance -ne 0 -or [decimal]$_.crossUnPnl -ne 0 } |
    Format-Table asset, balance, crossWalletBalance, crossUnPnl, availableBalance -AutoSize
