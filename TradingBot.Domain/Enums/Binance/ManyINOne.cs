namespace TradingBot.Domain.Enums.Binance;

public enum OrderResponseType { ACK, RESULT, FULL }
public enum CancelReplaceMode { STOP_ON_FAILURE, ALLOW_FAILURE }
public enum SelfTradePreventionMode { NONE, CANCEL_MAKER, CANCEL_TAKER, CANCEL_BOTH }
public enum PegPriceType { PRIMARY_PEG, MARKET_PEG }
public enum PegOffsetType { PRICE_LEVEL }
public enum CancelRestriction { ONLY_NEW, ONLY_PARTIALLY_FILLED }
public enum OrderRateLimitExceededMode { DO_NOTHING, CANCEL_ONLY }