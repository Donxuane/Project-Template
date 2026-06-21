using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class CurrentOpportunityScannerV1MarketContext
{
    private readonly Dictionary<string, SymbolIntervalSlice> _slices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CrossSymbolComboKey, NoPaidDataShortWindowFlowResearchV1CrossSymbolSimulator.GeometryScan> _scans = new();
    private readonly Dictionary<string, FuturesSymbolFilters> _filters = new(StringComparer.OrdinalIgnoreCase);

    public DateTime EvalUtc { get; private set; }
    public DateTime StudyStartUtc { get; private set; }
    public BtcContextIndex BtcContext { get; private set; } = null!;

    public static async Task<CurrentOpportunityScannerV1MarketContext> BuildAsync(
        BacktestSettings settings,
        DateTime? studyStartUtc,
        CancellationToken cancellationToken)
    {
        var loader = new HistoricalKlineDataLoader(settings);
        var btcData = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (btcData.Candles.Count == 0)
            throw new InvalidOperationException("BTCUSDT local candle data required for opportunity scanner.");

        var symbolData = new Dictionary<TradingSymbol, SymbolValidationResult>();
        foreach (var symbol in NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.Symbols)
        {
            var data = await loader.LoadAndValidateAsync(symbol, cancellationToken);
            if (data.Candles.Count == 0)
                throw new InvalidOperationException($"{symbol} local candle data required for opportunity scanner.");
            symbolData[symbol] = data;
        }

        symbolData[TradingSymbol.BTCUSDT] = btcData;
        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(symbolData);
        var evalUtc = ConfirmedClosedEvalUtcResolver.Resolve(symbolData, DateTime.UtcNow);
        var spanDays = (int)Math.Max(1, (evalUtc - dataStartUtc).TotalDays);
        var windowStart = evalUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var studyStart = studyStartUtc ?? evalUtc.AddDays(-38);
        if (studyStart >= evalUtc)
            studyStart = evalUtc.AddDays(-1);

        var ctx = new CurrentOpportunityScannerV1MarketContext
        {
            EvalUtc = evalUtc,
            StudyStartUtc = studyStart
        };

        var windowBtc = CandleWindowSlicer.Slice(btcData.Candles, windowStart, evalUtc);
        ctx.BtcContext = new BtcContextIndex(windowBtc);
        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);

        foreach (var symbol in NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.Symbols)
        {
            foreach (var interval in NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.Intervals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var windowSymbol = CandleWindowSlicer.Slice(symbolData[symbol].Candles, windowStart, evalUtc);
                var intervalCandles = CandleAggregator.Aggregate(symbol, windowSymbol, "1m", interval).Candles;
                var marketWideContext = new MarketWideContextIndex(
                    new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>
                    {
                        [symbol] = windowSymbol,
                        [TradingSymbol.BTCUSDT] = windowBtc
                    },
                    includeBtcInProxy: true);
                var flowIndex = new ShortWindowFlowFeatureIndex(futuresLoader, symbol, intervalCandles, windowBtc);

                var sliceKey = SliceKey(symbol, interval);
                ctx._slices[sliceKey] = new SymbolIntervalSlice(windowSymbol, intervalCandles, flowIndex, marketWideContext);

                var scans = NoPaidDataShortWindowFlowResearchV1CrossSymbolSimulator.ScanSymbolInterval(
                    symbol,
                    interval,
                    intervalCandles,
                    windowSymbol,
                    flowIndex,
                    ctx.BtcContext,
                    marketWideContext,
                    windowStart,
                    evalUtc,
                    cancellationToken);

                foreach (var scan in scans)
                    ctx._scans[scan.Key] = scan;
            }

            var filters = await FuturesTestnetShadowExchangeInfo.GetFiltersAsync(symbol.ToString(), cancellationToken);
            ctx._filters[symbol.ToString()] = filters;
        }

        return ctx;
    }

    public bool TryGetScan(CrossSymbolComboKey key, out NoPaidDataShortWindowFlowResearchV1CrossSymbolSimulator.GeometryScan scan)
        => _scans.TryGetValue(key, out scan!);

    public IReadOnlyList<KlineCandle> GetIntervalCandles(TradingSymbol symbol, string interval)
        => _slices[SliceKey(symbol, interval)].IntervalCandles;

    public ShortWindowFlowFeatureIndex GetFlowIndex(TradingSymbol symbol, string interval)
        => _slices[SliceKey(symbol, interval)].FlowIndex;

    public MarketWideContextIndex GetMarketWideContext(TradingSymbol symbol, string interval)
        => _slices[SliceKey(symbol, interval)].MarketWideContext;

    public IReadOnlyList<KlineCandle> GetWindowSymbolCandles(TradingSymbol symbol, string interval)
        => _slices[SliceKey(symbol, interval)].WindowSymbol;

    public string EvaluatePrecisionStatus(TradingSymbol symbol, decimal? entryPrice)
    {
        if (entryPrice is null or <= 0m)
            return "NoEntryPrice";

        if (!_filters.TryGetValue(symbol.ToString(), out var filters))
            return "UnknownSymbol";

        var notional = CurrentOpportunityScannerV1Catalog.DefaultShadowNotionalUsdt;
        var quantityRaw = notional / entryPrice.Value;
        var (_, valid) = FuturesTestnetShadowExchangeInfo.RoundQuantity(quantityRaw, filters);
        if (!valid)
            return "Invalid";

        return "Valid";
    }

    private static string SliceKey(TradingSymbol symbol, string interval)
        => $"{symbol}|{interval}";

    private sealed record SymbolIntervalSlice(
        IReadOnlyList<KlineCandle> WindowSymbol,
        IReadOnlyList<KlineCandle> IntervalCandles,
        ShortWindowFlowFeatureIndex FlowIndex,
        MarketWideContextIndex MarketWideContext);
}
