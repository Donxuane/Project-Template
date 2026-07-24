function Get-FuturesTestnetSettings {
    $settingsPath = Join-Path $PSScriptRoot '..\TradingBot\appsettings.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json

    $baseUrl = if ($env:FuturesTestnet__BaseUrl) {
        $env:FuturesTestnet__BaseUrl
    } else {
        $settings.FuturesTestnet.BaseUrl
    }
    $apiKey = $env:FuturesTestnet__ApiKey
    $secretKey = $env:FuturesTestnet__SecretKey

    if (-not $apiKey -or -not $secretKey) {
        throw 'Set FuturesTestnet__ApiKey and FuturesTestnet__SecretKey before running this tool.'
    }

    $uri = [Uri]$baseUrl
    $allowedHosts = @('demo-fapi.binance.com', 'testnet.binancefuture.com')
    if ($uri.Scheme -ne 'https' -or $allowedHosts -notcontains $uri.Host) {
        throw "Refusing non-testnet Futures URL: $baseUrl"
    }

    [pscustomobject]@{
        BaseUrl = $baseUrl.TrimEnd('/')
        ApiKey = $apiKey
        SecretKey = $secretKey
    }
}
