using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Application.SpotFuturesCrossMarket;

public sealed class AdaptiveRollingFuturesMarketDataService(
    ILogger<AdaptiveRollingFuturesMarketDataService> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<TradingSymbol, SymbolSocketState> _symbols = new();

    public Task EnsureSubscribedAsync(
        TradingSymbol symbol,
        AdaptiveRollingProfitExitV1Settings settings,
        CancellationToken cancellationToken)
    {
        var state = _symbols.GetOrAdd(symbol, _ => new SymbolSocketState(symbol));
        lock (state.Sync)
        {
            state.PruneWindowSeconds = Math.Max(settings.FlowWindowSeconds, settings.VelocityWindowSeconds) + 30;

            if (state.ReaderTask is { IsCompleted: false })
                return Task.CompletedTask;

            state.Cancellation?.Cancel();
            state.Cancellation?.Dispose();
            state.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            state.ReaderTask = Task.Run(() => RunSymbolLoopAsync(state, settings, state.Cancellation.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public AdaptiveRollingMarketDataSnapshot GetSnapshot(
        TradingSymbol symbol,
        OrderSide positionSide,
        decimal closeQuantity,
        AdaptiveRollingProfitExitV1Settings settings)
    {
        if (!_symbols.TryGetValue(symbol, out var state))
            return AdaptiveRollingMarketDataSnapshot.Invalid(symbol, "WebSocketNotSubscribed");

        lock (state.Sync)
        {
            var now = DateTime.UtcNow;
            var requiredTimes = new[]
            {
                state.LastBookTickerLocalReceiptUtc,
                state.LastDepthLocalReceiptUtc,
                state.LastAggTradeLocalReceiptUtc,
                state.LastMarkPriceLocalReceiptUtc
            };

            if (requiredTimes.Any(x => !x.HasValue))
            {
                var missing = new List<string>(4);
                if (!state.LastBookTickerLocalReceiptUtc.HasValue) missing.Add("bookTicker");
                if (!state.LastDepthLocalReceiptUtc.HasValue) missing.Add("depth");
                if (!state.LastAggTradeLocalReceiptUtc.HasValue) missing.Add("aggTrade");
                if (!state.LastMarkPriceLocalReceiptUtc.HasValue) missing.Add("markPrice");
                return state.ToInvalidSnapshot($"MissingRequiredStream:{string.Join(",", missing)}", now);
            }

            var maxAgeMs = requiredTimes
                .Where(x => x.HasValue)
                .Select(x => (long)Math.Max(0, (now - x!.Value).TotalMilliseconds))
                .DefaultIfEmpty(long.MaxValue)
                .Max();

            var latestEventTime = MaxUtc(
                state.LastBookTickerEventTimeUtc,
                state.LastDepthEventTimeUtc,
                state.LastAggTradeEventTimeUtc,
                state.LastMarkPriceEventTimeUtc);

            var latestTransactionTime = MaxUtc(
                state.LastBookTickerTransactionTimeUtc,
                state.LastDepthTransactionTimeUtc,
                state.LastAggTradeTransactionTimeUtc);

            var latestReceipt = MaxUtc(
                state.LastBookTickerLocalReceiptUtc,
                state.LastDepthLocalReceiptUtc,
                state.LastAggTradeLocalReceiptUtc,
                state.LastMarkPriceLocalReceiptUtc);

            var streamLatencyMs = latestEventTime.HasValue && latestReceipt.HasValue
                ? (long)Math.Max(0, (latestReceipt.Value - latestEventTime.Value).TotalMilliseconds)
                : 0L;

            if (!state.Connected)
                return state.ToInvalidSnapshot("WebSocketDisconnected", now, maxAgeMs, streamLatencyMs);
            if (state.SequenceInvalid)
                return state.ToInvalidSnapshot("DepthSequenceInvalid", now, maxAgeMs, streamLatencyMs);
            if (state.BestBidPrice <= 0m || state.BestAskPrice <= 0m || state.BestBidPrice >= state.BestAskPrice)
                return state.ToInvalidSnapshot("BookTickerCrossedOrIncomplete", now, maxAgeMs, streamLatencyMs);
            if (state.Bids.Count == 0 || state.Asks.Count == 0)
                return state.ToInvalidSnapshot("DepthIncomplete", now, maxAgeMs, streamLatencyMs);
            if (maxAgeMs > settings.MarketDataMaxAgeMs)
                return state.ToInvalidSnapshot("MarketDataStale", now, maxAgeMs, streamLatencyMs);

            var closeSideLevels = positionSide == OrderSide.BUY ? state.Bids : state.Asks;
            var estimatedVwap = EstimateVwap(closeSideLevels, closeQuantity, out var filledQty);
            if (filledQty < closeQuantity || estimatedVwap <= 0m)
                return state.ToInvalidSnapshot("DepthInsufficientForRemainingQuantity", now, maxAgeMs, streamLatencyMs);

            var bestExecutable = positionSide == OrderSide.BUY ? state.BestBidPrice : state.BestAskPrice;
            var estimatedSlippageBps = bestExecutable > 0m
                ? positionSide == OrderSide.BUY
                    ? Math.Max(0m, (bestExecutable - estimatedVwap) / bestExecutable * 10_000m)
                    : Math.Max(0m, (estimatedVwap - bestExecutable) / bestExecutable * 10_000m)
                : 0m;

            var mid = (state.BestBidPrice + state.BestAskPrice) / 2m;
            var spreadBps = mid > 0m ? (state.BestAskPrice - state.BestBidPrice) / mid * 10_000m : 0m;
            var topBidNotional = state.Bids.Take(5).Sum(x => x.Price * x.Quantity);
            var topAskNotional = state.Asks.Take(5).Sum(x => x.Price * x.Quantity);
            var imbalanceDenominator = topBidNotional + topAskNotional;
            var bookImbalance = imbalanceDenominator > 0m ? (topBidNotional - topAskNotional) / imbalanceDenominator : 0m;
            var microprice = state.BestBidQuantity + state.BestAskQuantity > 0m
                ? (state.BestAskPrice * state.BestBidQuantity + state.BestBidPrice * state.BestAskQuantity) /
                  (state.BestBidQuantity + state.BestAskQuantity)
                : mid;
            var micropricePressureBps = mid > 0m ? (microprice - mid) / mid * 10_000m : 0m;

            var cutoff = now.AddSeconds(-settings.FlowWindowSeconds);
            while (state.Trades.Count > 0 && state.Trades.Peek().ReceivedAtUtc < cutoff)
                state.Trades.Dequeue();

            var buyQty = state.Trades.Where(x => x.IsBuyerAggressor).Sum(x => x.Quantity);
            var sellQty = state.Trades.Where(x => !x.IsBuyerAggressor).Sum(x => x.Quantity);
            var flowDenominator = buyQty + sellQty;
            var flowImbalance = flowDenominator > 0m ? (buyQty - sellQty) / flowDenominator : 0m;

            var priceCutoff = now.AddSeconds(-settings.VelocityWindowSeconds);
            while (state.Prices.Count > 0 && state.Prices.Peek().ObservedAtUtc < priceCutoff)
                state.Prices.Dequeue();

            var velocityBps = 0m;
            var realizedVolatilityBps = 0m;
            if (state.Prices.Count >= 2)
            {
                var first = state.Prices.Peek();
                var last = state.Prices.Last();
                velocityBps = first.Price > 0m ? (last.Price - first.Price) / first.Price * 10_000m : 0m;
                realizedVolatilityBps = RealizedVolatilityBps(state.Prices.Select(x => x.Price).ToArray());
            }

            var valid = streamLatencyMs <= settings.StreamLatencyDegradedMs;
            return new AdaptiveRollingMarketDataSnapshot
            {
                Symbol = symbol,
                IsFresh = valid,
                DegradedReason = valid ? null : "StreamLatencyHigh",
                BestBidPrice = state.BestBidPrice,
                BestBidQuantity = state.BestBidQuantity,
                BestAskPrice = state.BestAskPrice,
                BestAskQuantity = state.BestAskQuantity,
                MarkPrice = state.MarkPrice,
                IndexPrice = state.IndexPrice,
                FundingRate = state.FundingRate,
                EstimatedCloseVwap = estimatedVwap,
                EstimatedCloseQuantity = filledQty,
                SpreadBps = spreadBps,
                EstimatedSlippageBps = estimatedSlippageBps,
                TopBidNotional = topBidNotional,
                TopAskNotional = topAskNotional,
                OrderBookImbalance = bookImbalance,
                Microprice = microprice,
                MicropricePressureBps = micropricePressureBps,
                AggressiveBuyQuantity = buyQty,
                AggressiveSellQuantity = sellQty,
                AggressiveFlowImbalance = flowImbalance,
                VelocityBps = velocityBps,
                RealizedVolatilityBps = realizedVolatilityBps,
                MarketDataEventTimeUtc = latestEventTime,
                MarketDataTransactionTimeUtc = latestTransactionTime,
                MarketDataLocalReceiptUtc = latestReceipt,
                MarketDataAgeMs = maxAgeMs,
                StreamLatencyMs = streamLatencyMs,
                LastError = state.LastError
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var state in _symbols.Values)
        {
            state.Cancellation?.Cancel();
        }

        foreach (var state in _symbols.Values)
        {
            try
            {
                if (state.ReaderTask is not null)
                    await state.ReaderTask.ConfigureAwait(false);
            }
            catch
            {
                // Shutdown should not fail the host.
            }

            state.Cancellation?.Dispose();
        }
    }

    private async Task RunSymbolLoopAsync(
        SymbolSocketState state,
        AdaptiveRollingProfitExitV1Settings settings,
        CancellationToken cancellationToken)
    {
        var delayMs = settings.WebSocketReconnectMinDelayMs;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSingleConnectionAsync(state, settings, cancellationToken);
                delayMs = settings.WebSocketReconnectMinDelayMs;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                lock (state.Sync)
                {
                    state.Connected = false;
                    state.LastDisconnectedAtUtc = DateTime.UtcNow;
                    state.LastError = ex.Message;
                }

                logger.LogWarning(
                    ex,
                    "AdaptiveRollingProfitExitV1 futures WebSocket disconnected. Symbol={Symbol} ReconnectDelayMs={DelayMs}",
                    state.Symbol,
                    delayMs);

                try
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                delayMs = Math.Min(settings.WebSocketReconnectMaxDelayMs, delayMs * 2);
            }
        }
    }

    private async Task RunSingleConnectionAsync(
        SymbolSocketState state,
        AdaptiveRollingProfitExitV1Settings settings,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        var symbol = state.Symbol.ToString().ToLowerInvariant();
        var streams = string.Join('/',
            $"{symbol}@bookTicker",
            $"{symbol}@depth20@100ms",
            $"{symbol}@aggTrade",
            $"{symbol}@markPrice@1s");
        var url = BuildCombinedStreamUrl(settings.WebSocketBaseUrl, streams);

        await socket.ConnectAsync(url, cancellationToken);
        lock (state.Sync)
        {
            state.Connected = true;
            state.LastConnectedAtUtc = DateTime.UtcNow;
            state.LastError = null;
        }

        logger.LogInformation(
            "AdaptiveRollingProfitExitV1 futures WebSocket connected. Symbol={Symbol} Streams={Streams}",
            state.Symbol,
            streams);

        var buffer = new byte[32 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "close", cancellationToken);
                    return;
                }

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            var json = Encoding.UTF8.GetString(message.ToArray());
            ProcessMessage(state, json, DateTime.UtcNow);
        }
    }

    private void ProcessMessage(SymbolSocketState state, string json, DateTime receivedAtUtc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var payload = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;
            var eventType = GetString(payload, "e");
            if (string.IsNullOrWhiteSpace(eventType))
                return;

            lock (state.Sync)
            {
                switch (eventType)
                {
                    case "bookTicker":
                        ApplyBookTicker(state, payload, receivedAtUtc);
                        break;
                    case "depthUpdate":
                        ApplyDepth(state, payload, receivedAtUtc);
                        break;
                    case "aggTrade":
                        ApplyAggTrade(state, payload, receivedAtUtc);
                        break;
                    case "markPriceUpdate":
                        ApplyMarkPrice(state, payload, receivedAtUtc);
                        break;
                }
            }
        }
        catch (JsonException ex)
        {
            lock (state.Sync)
            {
                state.LastError = $"JsonParseFailed:{ex.Message}";
            }
        }
    }

    private static void ApplyBookTicker(SymbolSocketState state, JsonElement payload, DateTime receivedAtUtc)
    {
        state.BestBidPrice = GetDecimal(payload, "b");
        state.BestBidQuantity = GetDecimal(payload, "B");
        state.BestAskPrice = GetDecimal(payload, "a");
        state.BestAskQuantity = GetDecimal(payload, "A");
        state.LastBookTickerEventTimeUtc = FromUnixMs(GetLong(payload, "E"));
        state.LastBookTickerTransactionTimeUtc = FromUnixMs(GetLong(payload, "T"));
        state.LastBookTickerLocalReceiptUtc = receivedAtUtc;

        var mid = (state.BestBidPrice + state.BestAskPrice) / 2m;
        if (mid > 0m)
            state.Prices.Enqueue(new PriceObservation(mid, receivedAtUtc));

        var cutoff = receivedAtUtc.AddSeconds(-state.PruneWindowSeconds);
        while (state.Prices.Count > 0 && state.Prices.Peek().ObservedAtUtc < cutoff)
            state.Prices.Dequeue();
    }

    private static void ApplyDepth(SymbolSocketState state, JsonElement payload, DateTime receivedAtUtc)
    {
        var finalUpdateId = GetLong(payload, "u");
        if (state.LastDepthFinalUpdateId > 0 && finalUpdateId > 0 && finalUpdateId <= state.LastDepthFinalUpdateId)
            state.SequenceInvalid = true;
        state.LastDepthFinalUpdateId = Math.Max(state.LastDepthFinalUpdateId, finalUpdateId);

        state.Bids = ParseLevels(payload, "b", descending: true);
        state.Asks = ParseLevels(payload, "a", descending: false);
        state.LastDepthEventTimeUtc = FromUnixMs(GetLong(payload, "E"));
        state.LastDepthTransactionTimeUtc = FromUnixMs(GetLong(payload, "T"));
        state.LastDepthLocalReceiptUtc = receivedAtUtc;

        if (state.Bids.Count > 0 && state.Asks.Count > 0 && state.Bids[0].Price >= state.Asks[0].Price)
            state.SequenceInvalid = true;
    }

    private static void ApplyAggTrade(SymbolSocketState state, JsonElement payload, DateTime receivedAtUtc)
    {
        var buyerIsMaker = GetBool(payload, "m");
        var quantity = GetDecimal(payload, "q");
        state.Trades.Enqueue(new TradeObservation(quantity, IsBuyerAggressor: !buyerIsMaker, receivedAtUtc));
        var cutoff = receivedAtUtc.AddSeconds(-state.PruneWindowSeconds);
        while (state.Trades.Count > 0 && state.Trades.Peek().ReceivedAtUtc < cutoff)
            state.Trades.Dequeue();
        state.LastAggTradeEventTimeUtc = FromUnixMs(GetLong(payload, "E"));
        state.LastAggTradeTransactionTimeUtc = FromUnixMs(GetLong(payload, "T"));
        state.LastAggTradeLocalReceiptUtc = receivedAtUtc;
    }

    private static void ApplyMarkPrice(SymbolSocketState state, JsonElement payload, DateTime receivedAtUtc)
    {
        state.MarkPrice = GetDecimal(payload, "p");
        state.IndexPrice = GetDecimal(payload, "i");
        state.FundingRate = GetDecimal(payload, "r");
        state.LastMarkPriceEventTimeUtc = FromUnixMs(GetLong(payload, "E"));
        state.LastMarkPriceLocalReceiptUtc = receivedAtUtc;
    }

    private static Uri BuildCombinedStreamUrl(string baseUrl, string streams)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var trimmed = baseUrl.TrimEnd('/');
        return new Uri($"{trimmed}{separator}streams={Uri.EscapeDataString(streams).Replace("%2F", "/", StringComparison.Ordinal)}");
    }

    private static List<OrderBookLevel> ParseLevels(JsonElement payload, string name, bool descending)
    {
        var levels = new List<OrderBookLevel>();
        if (!payload.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return levels;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
                continue;
            var price = ParseDecimal(item[0]);
            var qty = ParseDecimal(item[1]);
            if (price > 0m && qty > 0m)
                levels.Add(new OrderBookLevel(price, qty));
        }

        return descending
            ? levels.OrderByDescending(x => x.Price).ToList()
            : levels.OrderBy(x => x.Price).ToList();
    }

    private static decimal EstimateVwap(IReadOnlyList<OrderBookLevel> levels, decimal quantity, out decimal filledQty)
    {
        filledQty = 0m;
        var quote = 0m;
        foreach (var level in levels)
        {
            if (filledQty >= quantity)
                break;
            var take = Math.Min(quantity - filledQty, level.Quantity);
            quote += take * level.Price;
            filledQty += take;
        }

        return filledQty > 0m ? quote / filledQty : 0m;
    }

    private static decimal RealizedVolatilityBps(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < 3)
            return 0m;

        var returns = new List<decimal>();
        for (var i = 1; i < prices.Count; i++)
        {
            if (prices[i - 1] > 0m)
                returns.Add((prices[i] - prices[i - 1]) / prices[i - 1] * 10_000m);
        }

        if (returns.Count < 2)
            return 0m;

        var mean = returns.Average();
        var variance = returns.Sum(x => (x - mean) * (x - mean)) / returns.Count;
        return (decimal)Math.Sqrt((double)variance);
    }

    private static DateTime? MaxUtc(params DateTime?[] values)
        => values.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty().Max() == default
            ? null
            : values.Where(x => x.HasValue).Select(x => x!.Value).Max();

    private static DateTime? FromUnixMs(long ms)
        => ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static long GetLong(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
            return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : 0,
            JsonValueKind.String => long.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 0,
            _ => 0
        };
    }

    private static decimal GetDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
            return 0m;
        return ParseDecimal(v);
    }

    private static decimal ParseDecimal(JsonElement el)
        => el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m,
            _ => 0m
        };

    private sealed class SymbolSocketState(TradingSymbol symbol)
    {
        public TradingSymbol Symbol { get; } = symbol;
        public object Sync { get; } = new();
        public int PruneWindowSeconds { get; set; } = 120;
        public CancellationTokenSource? Cancellation { get; set; }
        public Task? ReaderTask { get; set; }
        public bool Connected { get; set; }
        public bool SequenceInvalid { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastConnectedAtUtc { get; set; }
        public DateTime? LastDisconnectedAtUtc { get; set; }
        public decimal BestBidPrice { get; set; }
        public decimal BestBidQuantity { get; set; }
        public decimal BestAskPrice { get; set; }
        public decimal BestAskQuantity { get; set; }
        public decimal MarkPrice { get; set; }
        public decimal IndexPrice { get; set; }
        public decimal FundingRate { get; set; }
        public long LastDepthFinalUpdateId { get; set; }
        public List<OrderBookLevel> Bids { get; set; } = [];
        public List<OrderBookLevel> Asks { get; set; } = [];
        public Queue<TradeObservation> Trades { get; } = new();
        public Queue<PriceObservation> Prices { get; } = new();
        public DateTime? LastBookTickerEventTimeUtc { get; set; }
        public DateTime? LastBookTickerTransactionTimeUtc { get; set; }
        public DateTime? LastBookTickerLocalReceiptUtc { get; set; }
        public DateTime? LastDepthEventTimeUtc { get; set; }
        public DateTime? LastDepthTransactionTimeUtc { get; set; }
        public DateTime? LastDepthLocalReceiptUtc { get; set; }
        public DateTime? LastAggTradeEventTimeUtc { get; set; }
        public DateTime? LastAggTradeTransactionTimeUtc { get; set; }
        public DateTime? LastAggTradeLocalReceiptUtc { get; set; }
        public DateTime? LastMarkPriceEventTimeUtc { get; set; }
        public DateTime? LastMarkPriceLocalReceiptUtc { get; set; }

        public AdaptiveRollingMarketDataSnapshot ToInvalidSnapshot(
            string reason,
            DateTime now,
            long marketDataAgeMs = long.MaxValue,
            long streamLatencyMs = 0)
            => new()
            {
                Symbol = Symbol,
                IsFresh = false,
                DegradedReason = reason,
                BestBidPrice = BestBidPrice,
                BestBidQuantity = BestBidQuantity,
                BestAskPrice = BestAskPrice,
                BestAskQuantity = BestAskQuantity,
                MarkPrice = MarkPrice,
                IndexPrice = IndexPrice,
                FundingRate = FundingRate,
                MarketDataLocalReceiptUtc = now,
                MarketDataAgeMs = marketDataAgeMs,
                StreamLatencyMs = streamLatencyMs,
                LastError = LastError
            };
    }

    private sealed record TradeObservation(decimal Quantity, bool IsBuyerAggressor, DateTime ReceivedAtUtc);
    private sealed record PriceObservation(decimal Price, DateTime ObservedAtUtc);
}

public sealed record OrderBookLevel(decimal Price, decimal Quantity);

public sealed class AdaptiveRollingMarketDataSnapshot
{
    public TradingSymbol Symbol { get; init; }
    public bool IsFresh { get; init; }
    public string? DegradedReason { get; init; }
    public decimal BestBidPrice { get; init; }
    public decimal BestBidQuantity { get; init; }
    public decimal BestAskPrice { get; init; }
    public decimal BestAskQuantity { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal IndexPrice { get; init; }
    public decimal FundingRate { get; init; }
    public decimal EstimatedCloseVwap { get; init; }
    public decimal EstimatedCloseQuantity { get; init; }
    public decimal SpreadBps { get; init; }
    public decimal EstimatedSlippageBps { get; init; }
    public decimal TopBidNotional { get; init; }
    public decimal TopAskNotional { get; init; }
    public decimal OrderBookImbalance { get; init; }
    public decimal Microprice { get; init; }
    public decimal MicropricePressureBps { get; init; }
    public decimal AggressiveBuyQuantity { get; init; }
    public decimal AggressiveSellQuantity { get; init; }
    public decimal AggressiveFlowImbalance { get; init; }
    public decimal VelocityBps { get; init; }
    public decimal RealizedVolatilityBps { get; init; }
    public DateTime? MarketDataEventTimeUtc { get; init; }
    public DateTime? MarketDataTransactionTimeUtc { get; init; }
    public DateTime? MarketDataLocalReceiptUtc { get; init; }
    public long MarketDataAgeMs { get; init; }
    public long StreamLatencyMs { get; init; }
    public string? LastError { get; init; }

    public static AdaptiveRollingMarketDataSnapshot Invalid(TradingSymbol symbol, string reason)
        => new() { Symbol = symbol, IsFresh = false, DegradedReason = reason, MarketDataAgeMs = long.MaxValue };
}
