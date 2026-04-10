using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services;

public class OrderStatusService(IDbConnection connection, ILogger<OrderStatusService> logger) : IOrderStatusService
{
    public async Task<bool> TryUpdateProcessingStatusAsync(long orderId, ProcessingStatus expectedStatus, ProcessingStatus newStatus, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        var allowedRetry = (expectedStatus == ProcessingStatus.TradesSyncFailed && (newStatus == ProcessingStatus.TradesSyncPending || newStatus == ProcessingStatus.TradesSyncInProgress))
                           || (expectedStatus == ProcessingStatus.PositionUpdateFailed && newStatus == ProcessingStatus.PositionUpdating);

        if (!allowedRetry && newStatus <= expectedStatus)
        {
            logger.LogWarning(
                "Rejected non-forward transition for Order {OrderId}: {Expected} -> {New}",
                orderId, expectedStatus, newStatus);
            return false;
        }

        var resetRetry = newStatus == ProcessingStatus.TradesSynced;
        var sql = resetRetry
            ? """
              UPDATE orders
              SET processing_status = @NewStatus, sync_retry_count = 0, updated_at = @UpdatedAt
              WHERE id = @Id AND processing_status = @ExpectedStatus;
              """
            : """
              UPDATE orders
              SET processing_status = @NewStatus, updated_at = @UpdatedAt
              WHERE id = @Id AND processing_status = @ExpectedStatus;
              """;

        var param = new
        {
            Id = orderId,
            ExpectedStatus = (int)expectedStatus,
            NewStatus = (int)newStatus,
            UpdatedAt = DateTime.UtcNow
        };

        var conn = transaction?.Connection ?? connection;
        var rowsAffected = await conn.ExecuteAsync(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));

        if (rowsAffected == 0)
        {
            logger.LogDebug(
                "Optimistic concurrency: no row updated for Order {OrderId} (expected {Expected})",
                orderId, expectedStatus);
        }

        return rowsAffected > 0;
    }

    public async Task<bool> TrySetTradesSyncFailedAsync(long orderId, ProcessingStatus expectedStatus, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        const string sql = """
            UPDATE orders
            SET processing_status = @FailedStatus, sync_retry_count = sync_retry_count + 1, updated_at = @UpdatedAt
            WHERE id = @Id AND processing_status = @ExpectedStatus;
            """;

        var param = new
        {
            Id = orderId,
            ExpectedStatus = (int)expectedStatus,
            FailedStatus = (int)ProcessingStatus.TradesSyncFailed,
            UpdatedAt = DateTime.UtcNow
        };

        var conn = transaction?.Connection ?? connection;
        var rowsAffected = await conn.ExecuteAsync(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));

        if (rowsAffected > 0)
            logger.LogWarning("Order {OrderId} set to TradesSyncFailed (from {Expected})", orderId, expectedStatus);

        return rowsAffected > 0;
    }

    public async Task<bool> TrySetPositionUpdateFailedAsync(long orderId, ProcessingStatus expectedStatus, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        const string sql = """
            UPDATE orders
            SET processing_status = @FailedStatus, updated_at = @UpdatedAt
            WHERE id = @Id AND processing_status = @ExpectedStatus;
            """;

        var param = new
        {
            Id = orderId,
            ExpectedStatus = (int)expectedStatus,
            FailedStatus = (int)ProcessingStatus.PositionUpdateFailed,
            UpdatedAt = DateTime.UtcNow
        };

        var conn = transaction?.Connection ?? connection;
        var rowsAffected = await conn.ExecuteAsync(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));

        if (rowsAffected > 0)
            logger.LogWarning("Order {OrderId} set to PositionUpdateFailed (from {Expected})", orderId, expectedStatus);

        return rowsAffected > 0;
    }
}
