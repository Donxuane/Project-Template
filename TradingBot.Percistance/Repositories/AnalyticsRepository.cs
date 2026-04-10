using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Analytics;
using TradingBot.Shared.Shared.Settings;

namespace TradingBot.Percistance.Repositories;

public class AnalyticsRepository(IOptions<ConnectionStrings> connectionString) : IAnalyticsRepository
{
    public async Task StoreAnalytics(TradeAnalyticsSummary tradeAnalyticsSummary)
    {
        var command = @"
            INSERT INTO analytics(
                totalpnl,
                winrate,
                averagewin,
                averageloss,
                totaltrades,
                maxdrawdown
            )
            VALUES(
                @TotalPnl,
                @WinRate,
                @AverageWin,
                @AverageLoss,
                @TotalTrades,
                @MaxDrawdown
            );";

        using var connection = new NpgsqlConnection(connectionString.Value.MainStorage);
        await connection.OpenAsync();
        await connection.ExecuteAsync(command, tradeAnalyticsSummary);
    }
}
