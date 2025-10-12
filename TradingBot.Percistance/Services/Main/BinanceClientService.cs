using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Shared.Shared.Models;

namespace TradingBot.Percistance.Services.Main;

public class BinanceClientService : IBinanceClientService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private static string? _apiKey;
    private static string? _secretKey;

    public BinanceClientService(IConfiguration configuration, HttpClient httpClient)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        var baseUrl = configuration.GetSection("BaseURL").Get<string>();
        _apiKey = configuration.GetSection("ApiKey").Get<string>();
        _secretKey = configuration.GetSection("SecretKey").Get<string>();
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("X-MBX-APIKEY", _apiKey);
    }
    public async Task<TResponse> Call<TResponse, TRequest>(TRequest? request, Endpoint endpoint, bool enableSignature)
    {
        if (string.IsNullOrWhiteSpace(endpoint.API))
            throw new ArgumentException("Endpoint API path is missing");

        var requestDict = request == null
        ? new Dictionary<string, string>()
        : request.GetType()
            .GetProperties()
            .Where(p => p.GetValue(request) != null)
            .ToDictionary(
                p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                     ?? char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1),
                p =>
                {
                    var value = p.GetValue(request);
                    if(value is bool val)
                    {
                        return val.ToString().ToLower();
                    }
                    return value.ToString()!;
                }
            );

        string queryString = string.Join("&", requestDict.Select(kv => $"{kv.Key}={kv.Value}"));


        if (enableSignature)
        {
            string signature = CreateSignature(queryString);
            queryString += $"&signature={signature}";
        }


        HttpResponseMessage response;

        if (endpoint.Type?.Equals("GET", StringComparison.OrdinalIgnoreCase) == true)
        {
            var fullUrl = string.IsNullOrEmpty(queryString)
                ? endpoint.API
                : $"{endpoint.API}?{queryString}";
            response = await _httpClient.GetAsync(fullUrl);
        }
        else if (endpoint.Type?.Equals("POST", StringComparison.OrdinalIgnoreCase) == true)
        {
            var content = new StringContent(queryString, Encoding.UTF8, "application/x-www-form-urlencoded");
            response = await _httpClient.PostAsync(endpoint.API, content);
        }
        else if (endpoint.Type?.Equals("DELETE", StringComparison.OrdinalIgnoreCase) == true)
        {
            var fullUrl = string.IsNullOrEmpty(queryString)
                ? endpoint.API
                : $"{endpoint.API}?{queryString}";
            response = await _httpClient.DeleteAsync(fullUrl);
        }
        else if(endpoint.Type?.Equals("PUT",StringComparison.OrdinalIgnoreCase) == true)
        {
            var content = new StringContent(queryString, Encoding.UTF8, "application/x-www-form-urlencoded");
            response = await _httpClient.PutAsync(endpoint.API, content);
        }
        else
        {
            throw new NotSupportedException($"HTTP method {endpoint.Request} not supported");
        }

        response.EnsureSuccessStatusCode();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var result = await response.Content.ReadFromJsonAsync<TResponse>(options);

        if (result == null)
            throw new Exception("Failed to deserialize response");

        return result;
    }
    public static string CreateSignature(string queryString)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(_secretKey);
        byte[] queryBytes = Encoding.UTF8.GetBytes(queryString);

        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(queryBytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}
