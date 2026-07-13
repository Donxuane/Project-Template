# Prints recent Binance USD-M Futures TESTNET income history (realized PnL, commissions, funding).
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
$startTime = $serverTime - (24 * 60 * 60 * 1000)
$query = 'symbol=BNBUSDT&startTime=' + $startTime + '&limit=50&timestamp=' + $serverTime + '&recvWindow=60000'
$sig = -join ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($query)) | ForEach-Object { $_.ToString('x2') })
$income = Invoke-RestMethod -Uri ($base + '/fapi/v1/income?' + $query + '&signature=' + $sig) -Headers @{ 'X-MBX-APIKEY' = $key }

$income | ForEach-Object { $_ } | ForEach-Object {
    [pscustomobject]@{
        TimeUtc    = [DateTimeOffset]::FromUnixTimeMilliseconds([long]$_.time).UtcDateTime.ToString('yyyy-MM-dd HH:mm:ss')
        IncomeType = $_.incomeType
        Income     = $_.income
        Asset      = $_.asset
        TradeId    = $_.tradeId
    }
} | Format-Table -AutoSize
