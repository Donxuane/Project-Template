using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AccountInformation;

namespace TradingBot.Application.BackgroundHostService.Services;

public sealed class SpotCommissionRateResolver(
    IConfiguration configuration,
    IToolService toolService,
    ITimeSyncService timeSyncService,
    ILogger<SpotCommissionRateResolver> logger) : ISpotCommissionRateResolver
{
    private static readonly ConcurrentDictionary<TradingSymbol, CommissionRateCacheEntry> Cache = new();

    public async Task<SpotCommissionRateResolution> ResolveFeeRatePercentAsync(
        TradingSymbol symbol,
        CancellationToken cancellationToken = default)
    {
        var cacheTtlSeconds = Math.Max(10, configuration.GetValue<int?>("Trading:CommissionCacheTtlSeconds") ?? 300);
        var nowUtc = DateTime.UtcNow;
        if (Cache.TryGetValue(symbol, out var cached) && cached.ExpiresAtUtc > nowUtc)
            return cached.Resolution;

        if (await TryResolveSymbolCommissionAsync(symbol, cancellationToken) is { } symbolResolution)
            return CacheAndReturn(symbol, symbolResolution, nowUtc, cacheTtlSeconds);

        if (await TryResolveAccountCommissionAsync(symbol, cancellationToken) is { } accountResolution)
            return CacheAndReturn(symbol, accountResolution, nowUtc, cacheTtlSeconds);

        var configuredFeeRatePercent = configuration.GetValue<decimal?>("Trading:FeeRatePercent");
        var fallbackResolution = new SpotCommissionRateResolution
        {
            FeeRatePercent = Math.Max(0m, configuredFeeRatePercent ?? 0.1m),
            FeeRateSource = configuredFeeRatePercent.HasValue ? "ConfigFallback" : "UnknownFallback"
        };
        return CacheAndReturn(symbol, fallbackResolution, nowUtc, cacheTtlSeconds);
    }

    private async Task<SpotCommissionRateResolution?> TryResolveSymbolCommissionAsync(
        TradingSymbol symbol,
        CancellationToken cancellationToken)
    {
        try
        {
            var timestamp = await timeSyncService.GetAdjustedTimestampAsync(cancellationToken);
            var endpoint = toolService.BinanceEndpointsService.GetEndpoint(Account.CommissionsRate);
            var response = await toolService.BinanceClientService.Call<CommissionResponse, AccountCommissionRequest>(
                new AccountCommissionRequest
                {
                    Symbol = symbol.ToString(),
                    Timestamp = timestamp,
                    RecvWindow = 30000
                },
                endpoint,
                true);

            if (TryParseRatePercentFromFraction(response?.StandardCommission?.Taker, out var takerPercent))
            {
                return new SpotCommissionRateResolution
                {
                    FeeRatePercent = takerPercent,
                    FeeRateSource = "BinanceSymbolCommission"
                };
            }

            if (TryParseRatePercentFromFraction(response?.StandardCommission?.Maker, out var makerPercent))
            {
                return new SpotCommissionRateResolution
                {
                    FeeRatePercent = makerPercent,
                    FeeRateSource = "BinanceSymbolCommission"
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "SpotCommissionRateResolver failed symbol commission lookup. Symbol={Symbol}",
                symbol);
        }

        return null;
    }

    private async Task<SpotCommissionRateResolution?> TryResolveAccountCommissionAsync(
        TradingSymbol symbol,
        CancellationToken cancellationToken)
    {
        try
        {
            var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync(cancellationToken);
            var endpoint = toolService.BinanceEndpointsService.GetEndpoint(Account.AccoutnInformation);
            var response = await toolService.BinanceClientService.Call<AccountInfoResponse, AccountInfoRequest>(
                new AccountInfoRequest
                {
                    OmitZeroBalances = true,
                    RecvWindow = 30000,
                    Timestamp = adjustedTimestamp
                },
                endpoint,
                true);

            if (TryParseRatePercentFromFraction(response?.CommissionRates?.Taker, out var takerPercent))
            {
                return new SpotCommissionRateResolution
                {
                    FeeRatePercent = takerPercent,
                    FeeRateSource = "BinanceAccountCommission"
                };
            }

            if (TryParseRatePercentFromFraction(response?.CommissionRates?.Maker, out var makerPercent))
            {
                return new SpotCommissionRateResolution
                {
                    FeeRatePercent = makerPercent,
                    FeeRateSource = "BinanceAccountCommission"
                };
            }

            if (response is not null)
            {
                var integerFallbackPercent = Math.Max(0m, response.TakerCommission) / 100m;
                return new SpotCommissionRateResolution
                {
                    FeeRatePercent = integerFallbackPercent,
                    FeeRateSource = "BinanceAccountCommission"
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "SpotCommissionRateResolver failed account commission lookup. Symbol={Symbol}",
                symbol);
        }

        return null;
    }

    private static bool TryParseRatePercentFromFraction(string? raw, out decimal percent)
    {
        percent = 0m;
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var fraction))
            return false;

        if (fraction < 0m)
            return false;

        percent = fraction * 100m;
        return true;
    }

    private static SpotCommissionRateResolution CacheAndReturn(
        TradingSymbol symbol,
        SpotCommissionRateResolution resolution,
        DateTime nowUtc,
        int ttlSeconds)
    {
        var cacheEntry = new CommissionRateCacheEntry(
            resolution,
            nowUtc.AddSeconds(ttlSeconds));
        Cache.AddOrUpdate(symbol, cacheEntry, (_, _) => cacheEntry);
        return resolution;
    }

    private sealed record CommissionRateCacheEntry(
        SpotCommissionRateResolution Resolution,
        DateTime ExpiresAtUtc);
}

