using System.Runtime.CompilerServices;
using System.Text.Json;
using TradingBot.Domain.Enums.AI;

namespace TradingBot.Domain.Extentions;

public static class OpenAIRequestBuilder
{
    public static string AISystemRequest(this string content, AiRequestModels model)
    {
        if(model == AiRequestModels.Buy_Sell)
        {
            content = @"You are a professional crypto trading analyst.
                        Analyze the provided market snapshot and recommend one of:
                        BUY, SELL, HOLD with a suggested quantity. Consider liquidity, 
                        recent trades, trend, and fair value. 
                        Based on provided Binance market data,
                        return ONLY one JSON with the following format:

                        {
                          ""action"": ""BUY"" | ""SELL"" | ""HOLD"",
                          ""symbol"": ""BTCUSDT"",
                          ""type"": ""LIMIT"" | ""MARKET"",
                          ""price"": number,
                          ""quantity"": number
                        }

                        Only output valid JSON. No text outside JSON.";
        }
        else
        {
            content = "Answer the question";
        }
        return content;
    }

    public static string AIUserRequest<TRequest>(this string content, AiRequestModels model,TRequest request)
    {
        if(model == AiRequestModels.Buy_Sell)
        {
            content = $"Here is the market data: {JsonSerializer.Serialize(request)}";
        }
        else
        {
            content = JsonSerializer.Serialize(request);
        }
            return content;
    }
}
