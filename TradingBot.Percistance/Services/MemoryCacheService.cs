﻿using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services;

public class MemoryCacheService(IMemoryCache _cache, ILogger<MemoryCacheService> logger) : IMemoryCacheService
{
    public List<object?>? GetAllCachedData(List<string> keys)
    {
        try
        {
            var allData = new List<object?>();
            foreach (var key in keys)
            {
                _cache.TryGetValue(key, out var value);
                allData.Add(value);
            }
            return allData;
        }catch (Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception: {ex},  
                  DateTime: {date}",
                ex.Message,
                DateTime.Now
            );
            return null;
        }
    }

    public object? GetCacheValue(string key)
    {
        try
        {
            _cache.TryGetValue(key, out var value);
            logger.LogInformation(
                @"Get Cached Data:
                model: {model}
                DateTime: {time}",
                value,
                DateTime.Now
            );
            return value;
        }
        catch(Exception ex)
        {
            logger.LogError(
                ex, 
                @"Exception: {ex},  
                  DateTime: {date}
                  Key: {key}", 
                ex.Message,
                DateTime.Now,
                key
            );
            return null;
        }
    }

    public void RemoveCacheValue(string key)
    {
        try
        {
            var value = GetCacheValue(key);
            if (value != null)
            {
                _cache.Remove(key);
                logger.LogInformation(
                    @"removed key: {key}
                    DateTime: {time}",
                    key,
                    DateTime.Now
                );
            }
        }
        catch(Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception: {ex},  
                  DateTime: {date}
                  Key: {key}",
                ex.Message,
                DateTime.Now,
                key
            );
        }
    }

    public object? SetCacheValue(string key, object value)
    {
        try
        {
            _cache.Set(key, value, TimeSpan.FromHours(24));
            logger.LogInformation(
                @"Cached Data
                model: {model}
                DateTime: {time}",
                value,
                DateTime.Now
            );
            return value;
        }
        catch(Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception: {ex},  
                  DateTime: {date}
                  Key: {key}",
                ex.Message,
                DateTime.Now,
                key
            );
            return null;
        }
    }
}
