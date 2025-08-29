namespace TradingBot.Domain.Enums.Endpoints;

public enum MarketData
{
    OrderBookDepth,
    RecentTrades,
    OldTradeLookUp,
    AgregateTrades,
    CandlestickDataKline,
    CurrentAveragePrice,
    _24hrTickerPriceChangeStats,
    SymbolPriceTicker,
    SymbolOrderBookTicker,
    RollingWindowTicker
}
