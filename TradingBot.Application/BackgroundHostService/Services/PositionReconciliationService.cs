using System.Globalization;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.BackgroundHostService.Services;

public sealed class PositionReconciliationService : IPositionReconciliationService
{
    public IReadOnlyList<ReconciliationResult> EvaluateSpot(
        IReadOnlyList<Position> openPositions,
        IReadOnlyList<Position> closedPositions,
        IReadOnlyList<Balance> exchangeBalances,
        IReadOnlyDictionary<string, BalanceSnapshot> latestSnapshotsByAsset,
        decimal tolerance,
        int maxOpenPositionsPerSymbol,
        TimeSpan snapshotMaxAge)
    {
        var normalizedTolerance = Math.Max(0m, tolerance);
        var results = new List<ReconciliationResult>();

        var balanceByAsset = exchangeBalances
            .GroupBy(b => b.Asset, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var free = g.Sum(x => ParseDecimal(x.Free));
                    var locked = g.Sum(x => ParseDecimal(x.Locked));
                    return (Free: free, Locked: locked, Total: free + locked);
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var duplicate in openPositions.GroupBy(p => p.Symbol).Where(g => g.Count() > maxOpenPositionsPerSymbol))
        {
            results.Add(new ReconciliationResult
            {
                Symbol = duplicate.Key,
                Asset = ResolveBaseAsset(duplicate.Key),
                LocalOpenQuantity = duplicate.Sum(x => Math.Max(0m, x.Quantity)),
                IsMatched = false,
                Severity = "Error",
                Reason = $"Multiple open positions found for symbol. Count={duplicate.Count()}, MaxAllowed={maxOpenPositionsPerSymbol}."
            });
        }

        foreach (var closedWithQuantity in closedPositions.Where(x => x.Quantity != 0m))
        {
            results.Add(new ReconciliationResult
            {
                Symbol = closedWithQuantity.Symbol,
                Asset = ResolveBaseAsset(closedWithQuantity.Symbol),
                LocalOpenQuantity = closedWithQuantity.Quantity,
                IsMatched = false,
                Severity = "Error",
                Reason = "Closed position has non-zero quantity."
            });
        }

        var openBySymbol = openPositions
            .GroupBy(x => x.Symbol)
            .ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0m, x.Quantity)));

        var knownAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (symbol, localQuantity) in openBySymbol)
        {
            var asset = ResolveBaseAsset(symbol);
            knownAssets.Add(asset);
            balanceByAsset.TryGetValue(asset, out var exchange);
            var difference = localQuantity - exchange.Total;

            var snapshotIssue = EvaluateSnapshotIssue(asset, latestSnapshotsByAsset, snapshotMaxAge);
            if (snapshotIssue is not null)
            {
                results.Add(new ReconciliationResult
                {
                    Symbol = symbol,
                    Asset = asset,
                    LocalOpenQuantity = localQuantity,
                    ExchangeFree = exchange.Free,
                    ExchangeLocked = exchange.Locked,
                    ExchangeTotal = exchange.Total,
                    Difference = difference,
                    IsMatched = false,
                    Severity = "Warning",
                    Reason = snapshotIssue
                });
            }

            if (exchange.Total <= normalizedTolerance)
            {
                results.Add(new ReconciliationResult
                {
                    Symbol = symbol,
                    Asset = asset,
                    LocalOpenQuantity = localQuantity,
                    ExchangeFree = exchange.Free,
                    ExchangeLocked = exchange.Locked,
                    ExchangeTotal = exchange.Total,
                    Difference = difference,
                    IsMatched = false,
                    Severity = "Error",
                    Reason = "Local open position exists but exchange asset balance is zero or below tolerance."
                });
                continue;
            }

            if (Math.Abs(difference) > normalizedTolerance)
            {
                results.Add(new ReconciliationResult
                {
                    Symbol = symbol,
                    Asset = asset,
                    LocalOpenQuantity = localQuantity,
                    ExchangeFree = exchange.Free,
                    ExchangeLocked = exchange.Locked,
                    ExchangeTotal = exchange.Total,
                    Difference = difference,
                    IsMatched = false,
                    Severity = "Warning",
                    Reason = "Local quantity differs from exchange total by more than tolerance."
                });
            }
            else
            {
                results.Add(new ReconciliationResult
                {
                    Symbol = symbol,
                    Asset = asset,
                    LocalOpenQuantity = localQuantity,
                    ExchangeFree = exchange.Free,
                    ExchangeLocked = exchange.Locked,
                    ExchangeTotal = exchange.Total,
                    Difference = difference,
                    IsMatched = true,
                    Severity = "Info",
                    Reason = "Matched within tolerance."
                });
            }
        }

        foreach (var (asset, exchange) in balanceByAsset)
        {
            if (exchange.Total <= normalizedTolerance)
                continue;
            if (asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!TryResolveSymbol(asset, out var symbol))
                continue;
            if (openBySymbol.ContainsKey(symbol))
                continue;

            knownAssets.Add(asset);
            var snapshotIssue = EvaluateSnapshotIssue(asset, latestSnapshotsByAsset, snapshotMaxAge);
            var reason = snapshotIssue is null
                ? "Exchange asset balance exists but no local open position exists."
                : $"Exchange asset balance exists but no local open position exists. {snapshotIssue}";

            results.Add(new ReconciliationResult
            {
                Symbol = symbol,
                Asset = asset,
                LocalOpenQuantity = 0m,
                ExchangeFree = exchange.Free,
                ExchangeLocked = exchange.Locked,
                ExchangeTotal = exchange.Total,
                Difference = -exchange.Total,
                IsMatched = false,
                Severity = "Warning",
                Reason = reason
            });
        }

        foreach (var asset in knownAssets)
        {
            var snapshotIssue = EvaluateSnapshotIssue(asset, latestSnapshotsByAsset, snapshotMaxAge);
            if (snapshotIssue is null)
                continue;

            var alreadyReported = results.Any(x =>
                x.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase)
                && x.Reason.Contains(snapshotIssue, StringComparison.OrdinalIgnoreCase));
            if (alreadyReported)
                continue;

            results.Add(new ReconciliationResult
            {
                Asset = asset,
                IsMatched = false,
                Severity = "Warning",
                Reason = snapshotIssue
            });
        }

        return results;
    }

    private static string? EvaluateSnapshotIssue(
        string asset,
        IReadOnlyDictionary<string, BalanceSnapshot> latestSnapshotsByAsset,
        TimeSpan snapshotMaxAge)
    {
        if (!latestSnapshotsByAsset.TryGetValue(asset, out var snapshot))
            return "Balance snapshot is missing for asset.";

        var updatedAt = snapshot.UpdatedAt == default ? snapshot.CreatedAt : snapshot.UpdatedAt;
        if (updatedAt == default)
            return "Balance snapshot has no timestamp.";

        var age = DateTime.UtcNow - updatedAt;
        if (age > snapshotMaxAge)
            return $"Balance snapshot is stale. AgeSeconds={Math.Round(age.TotalSeconds, 2)}.";

        return null;
    }

    private static decimal ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }

    private static string ResolveBaseAsset(TradingSymbol symbol)
        => symbol.ToString().Replace("USDT", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveSymbol(string asset, out TradingSymbol symbol)
        => Enum.TryParse($"{asset.ToUpperInvariant()}USDT", true, out symbol);
}
