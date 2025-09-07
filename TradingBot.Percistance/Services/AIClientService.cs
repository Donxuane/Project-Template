using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Enums.AI;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AI;
using TradingBot.Shared.Shared.Models;
namespace TradingBot.Percistance.Services;

public class AIClientService(
    ILogger<AIClientService> logger,
    HttpClient client,
    IConfiguration configuration) : IAICLinetService
{
    private AIEndpoint? AIEndpoint() => configuration.GetSection("OpenAISettings").Get<AIEndpoint>();

    public async Task<TResponse> Call<TResponse, TRequest>(TRequest request, AiRequestModels models)
    {

        var settings = AIEndpoint() ?? throw new Exception("Settings not found");
        string systemContent = "", userContent = "";
        systemContent = models == AiRequestModels.String ? "Answer question" : systemContent.AISystemRequest(models);
        userContent = userContent.AIUserRequest(models, request);

        var aiRequestBody = new
        {
            model = settings.Model,
            messages = new List<AIMessage>
                {
                    new() { Role = "system", Content = systemContent },
                    new() { Role = "user", Content = userContent }
                },
            max_tokens = 150,
            temperature = 0.2
        };
        var jsonBody = JsonSerializer.Serialize(aiRequestBody);
        var aiRequest = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseURI}{settings.Endpoint}");
        aiRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.APIKey);
        aiRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        var response = await client.SendAsync(aiRequest);
        response.EnsureSuccessStatusCode();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var result = await response.Content.ReadFromJsonAsync<TResponse>(options);
        return result;
    }
}
