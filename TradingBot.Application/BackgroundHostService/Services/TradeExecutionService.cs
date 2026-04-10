using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService.Services;

public class TradeExecutionService(
    IMediator mediator,
    IConfiguration configuration,
    ILogger<TradeExecutionService> logger) : ITradeExecutionService
{
    public async Task<TradeExecutionResult> ExecuteMarketOrderAsync(
        TradeExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var retryCount = Math.Max(0, configuration.GetValue<int?>("ExecutionSettings:Retry:Count") ?? 2);
        var baseDelayMs = Math.Max(100, configuration.GetValue<int?>("ExecutionSettings:Retry:BaseDelayMs") ?? 500);

        for (var attempt = 1; attempt <= retryCount + 1; attempt++)
        {
            try
            {
                var orderResult = await mediator.Send(
                    new PlaceSpotOrderCommand(
                        request.Symbol,
                        request.Side,
                        request.Quantity,
                        Price: null,
                        IsLimitOrder: false),
                    cancellationToken);

                if (orderResult.Success)
                {
                    logger.LogInformation(
                        "Trade execution success: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}, Attempt={Attempt}",
                        request.CorrelationId,
                        request.DecisionId,
                        request.Symbol,
                        request.Side,
                        orderResult.Order?.Id,
                        orderResult.Order?.ExchangeOrderId,
                        attempt);

                    return new TradeExecutionResult
                    {
                        Success = true,
                        LocalOrderId = orderResult.Order?.Id,
                        ExchangeOrderId = orderResult.Order?.ExchangeOrderId
                    };
                }

                if (attempt > retryCount || !IsTransientError(orderResult.Error))
                {
                    logger.LogWarning(
                        "Trade execution failed without retry: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, Attempt={Attempt}, Error={Error}",
                        request.CorrelationId,
                        request.DecisionId,
                        request.Symbol,
                        request.Side,
                        attempt,
                        orderResult.Error);

                    return new TradeExecutionResult
                    {
                        Success = false,
                        Error = orderResult.Error
                    };
                }

                var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
                logger.LogWarning(
                    "Trade execution transient failure, retrying: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, Attempt={Attempt}, DelayMs={DelayMs}, Error={Error}",
                    request.CorrelationId,
                    request.DecisionId,
                    request.Symbol,
                    request.Side,
                    attempt,
                    delay.TotalMilliseconds,
                    orderResult.Error);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsTransientException(ex) && attempt <= retryCount)
            {
                var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
                logger.LogWarning(
                    ex,
                    "Trade execution transient exception, retrying: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, Attempt={Attempt}, DelayMs={DelayMs}",
                    request.CorrelationId,
                    request.DecisionId,
                    request.Symbol,
                    request.Side,
                    attempt,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Trade execution fatal exception: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}",
                    request.CorrelationId,
                    request.DecisionId,
                    request.Symbol,
                    request.Side);

                return new TradeExecutionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        return new TradeExecutionResult
        {
            Success = false,
            Error = "Execution failed after retries."
        };
    }

    private static bool IsTransientException(Exception ex)
    {
        return ex is TimeoutException
            || ex is HttpRequestException
            || ex is IOException
            || ex is TaskCanceledException;
    }

    private static bool IsTransientError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        var text = error.ToLowerInvariant();
        return text.Contains("timeout")
               || text.Contains("temporar")
               || text.Contains("429")
               || text.Contains("too many request")
               || text.Contains("rate limit")
               || text.Contains("network")
               || text.Contains("unavailable");
    }
}
