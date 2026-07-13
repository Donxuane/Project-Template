# Prints recent Binance USD-M Futures TESTNET user trades (actual fills) for a symbol.
param([string]$Symbol = 'BNBUSDT')
$ErrorActionPreference = 'Stop'

$raw = Get-Content (Join-Path $PSScriptRoot '..\TradingBot\appsettings.json') |
    Where-Object { $_.TrimStart() -notmatch '^//' } | Out-String
$settings = $raw | ConvertFrom-Json
$key = $settings.Eth15TestnetExecution.TestnetApiKey
$secret = $settings.Eth15TestnetExecution.TestnetSecretKey
$base = $settings.Eth15TestnetExecution.TestnetBaseUrl

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [Text.Encoding]::UTF8.GetBytes($secret)

$serverTime = (Invoke-RestMethod ($base + '/fapi/v1/time')).serverTime
$query = 'symbol=' + $Symbol + '&limit=20&timestamp=' + $serverTime + '&recvWindow=60000'
$sig = -join ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($query)) | ForEach-Object { $_.ToString('x2') })
$trades = Invoke-RestMethod -Uri ($base + '/fapi/v1/userTrades?' + $query + '&signature=' + $sig) -Headers @{ 'X-MBX-APIKEY' = $key }

$trades | ForEach-Object { $_ } | ForEach-Object {
    [pscustomobject]@{
        TimeUtc    = [DateTimeOffset]::FromUnixTimeMilliseconds([long]$_.time).UtcDateTime.ToString('yyyy-MM-dd HH:mm:ss')
        TradeId    = $_.id
        OrderId    = $_.orderId
        Side       = $_.side
        Price      = $_.price
        Qty        = $_.qty
        QuoteQty   = $_.quoteQty
        Commission = $_.commission
        RealizedPnl = $_.realizedPnl
    }
} | Format-Table -AutoSize
