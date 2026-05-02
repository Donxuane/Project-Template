using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService.Services;

public class TradeExecutionService(
    IMediator mediator,
    IConfiguration configuration,
    IPositionRepository positionRepository,
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
                var parentPositionId = await ResolveParentPositionIdAsync(request, cancellationToken);
                var closeReason = request.ExecutionIntent == TradeExecutionIntent.CloseLong
                    ? CloseReason.OppositeSignal
                    : CloseReason.None;

                var orderResult = await mediator.Send(
                    new PlaceSpotOrderCommand(
                        request.Symbol,
                        request.Side,
                        request.Quantity,
                        Price: null,
                        IsLimitOrder: false,
                        OrderSource: OrderSource.DecisionWorker,
                        CloseReason: closeReason,
                        ParentPositionId: parentPositionId,
                        CorrelationId: request.CorrelationId),
                    cancellationToken);

                if (orderResult.Success)
                {
                    logger.LogInformation(
                        "Trade execution success: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}, Attempt={Attempt}",
                        request.CorrelationId,
                        request.DecisionId,
                        request.Symbol,
                        request.Side,
                        request.TradingMode,
                        request.ExecutionIntent,
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
                        "Trade execution failed without retry: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Attempt={Attempt}, Error={Error}",
                        request.CorrelationId,
                        request.DecisionId,
                        request.Symbol,
                        request.Side,
                        request.TradingMode,
                        request.ExecutionIntent,
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
                    "Trade execution transient failure, retrying: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Attempt={Attempt}, DelayMs={DelayMs}, Error={Error}",
                    request.CorrelationId,
                    request.DecisionId,
                    request.Symbol,
                    request.Side,
                    request.TradingMode,
                    request.ExecutionIntent,
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
                    "Trade execution transient exception, retrying: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Attempt={Attempt}, DelayMs={DelayMs}",
                    request.CorrelationId,
                    request.DecisionId,
                    request.Symbol,
                    request.Side,
                    request.TradingMode,
                    request.ExecutionIntent,
                    attempt,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Trade execution fatal exception: CorrelationId={CorrelationId}, DecisionId={DecisionId}, Symbol={Symbol}, Side={Side}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}",
                    request.CorrelationId,
                    request.DecisionId,
                    request.Symbol,
                    request.Side,
                    request.TradingMode,
                    request.ExecutionIntent);

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

    private async Task<long?> ResolveParentPositionIdAsync(TradeExecutionRequest request, CancellationToken cancellationToken)
    {
        if (request.TradingMode != TradingMode.Spot)
            return null;

        var openPosition = await positionRepository.GetOpenPositionAsync(request.Symbol, cancellationToken);
        if (openPosition is null)
            return null;

        return request.ExecutionIntent is TradeExecutionIntent.CloseLong or TradeExecutionIntent.OpenLong
            ? openPosition.Id
            : null;
    }
}
