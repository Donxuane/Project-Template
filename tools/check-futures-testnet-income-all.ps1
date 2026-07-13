# Full income for last 24h across all symbols (no symbol filter).
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
$query = 'startTime=' + $startTime + '&limit=1000&timestamp=' + $serverTime + '&recvWindow=60000'
$sig = -join ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($query)) | ForEach-Object { $_.ToString('x2') })
$income = @(Invoke-RestMethod -Uri ($base + '/fapi/v1/income?' + $query + '&signature=' + $sig) -Headers @{ 'X-MBX-APIKEY' = $key })

$rows = $income | ForEach-Object { $_ } | ForEach-Object {
    [pscustomobject]@{
        TimeUtc    = [DateTimeOffset]::FromUnixTimeMilliseconds([long]$_.time).UtcDateTime.ToString('yyyy-MM-dd HH:mm:ss')
        Symbol     = $_.symbol
        IncomeType = $_.incomeType
        Income     = [decimal]$_.income
        Asset      = $_.asset
    }
}
$rows | Format-Table -AutoSize

$byType = $rows | Group-Object IncomeType | ForEach-Object {
    [pscustomobject]@{ Type = $_.Name; Total = ($_.Group | Measure-Object Income -Sum).Sum; Count = $_.Count }
}
Write-Host '=== Totals by type ==='
$byType | Format-Table -AutoSize
$net = ($rows | Measure-Object Income -Sum).Sum
Write-Host ("Net income (24h): {0:F8} USDT" -f $net)
