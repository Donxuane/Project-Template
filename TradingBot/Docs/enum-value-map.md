# Enum Value Map for SQL Debugging

This document maps integer enum values (stored in PostgreSQL) to C# enum names used by the project.

- Source of truth: enum files in `TradingBot.Domain/Enums` (and `TradingBot.Domain/Enums/Binance`)
- Scope: enums used in `orders`, `trade_executions`, `trade_execution_decisions`, `positions`, plus requested companion enums.

> These values must stay in sync with C# enums. If enum numeric values change, historical database meaning can break.

## Common quick examples

- `guardstage = 6` -> `ConfidenceGate`
- `decisionstatus = 1` -> `Skipped`
- `order_source = 2` -> `TradeMonitorWorker`
- `close_reason = 1` -> `StopLoss`

---

## TradingSymbol

| Value | Name | Meaning |
|---:|---|---|
| 1 | BTCUSDT | Bitcoin vs USDT |
| 2 | ETHUSDT | Ethereum vs USDT |
| 3 | BNBUSDT | BNB vs USDT |
| 4 | XRPUSDT | XRP vs USDT |
| 5 | SOLUSDT | Solana vs USDT |
| 6 | DOGEUSDT | Dogecoin vs USDT |
| 7 | ADAUSDT | Cardano vs USDT |
| 8 | DOTUSDT | Polkadot vs USDT |
| 9 | SHIBUSDT | SHIB vs USDT |

## Assets

Large enum used by project; verify business meaning in code when needed.

| Value | Name | Meaning |
|---:|---|---|
| 1 | BTC | Asset code |
| 2 | ETH | Asset code |
| 3 | BNB | Asset code |
| 4 | USDT | Asset code |
| ... | ... | Large list; see `TradingBot.Domain/Enums/Assets.cs` |
| 467 | XAUT | Asset code |

## TradeSignal

Used for decision action/raw signal in `trade_execution_decisions`.

| Value | Name | Meaning |
|---:|---|---|
| 0 | Buy | Strategy suggests buy |
| 1 | Sell | Strategy suggests sell |
| 2 | Hold | Strategy suggests no trade |

## TradeAction / DecisionAction

No separate `TradeAction` or `DecisionAction` enum is currently defined.  
Decision action fields use `TradeSignal`.

## OrderSide

Used by `orders.side`, `trade_executions.side`, `positions.side`, and decision side fields.

| Value | Name | Meaning |
|---:|---|---|
| 0 | BUY | Buy side |
| 1 | SELL | Sell side |

## OrderStatuses

Used by `orders.status`.

| Value | Name | Meaning |
|---:|---|---|
| 0 | NEW | Order created on exchange |
| 1 | PARTIALLY_FILLED | Partially executed |
| 2 | FILLED | Fully executed |
| 3 | CANCELED | Canceled |
| 4 | REJECTED | Rejected |
| 5 | EXPIRED | Expired |
| 6 | EXPIRED_IN_MATCH | Expired during matching |
| 7 | PENDING_CANCEL | Cancel pending |
| 8 | PENDING_NEW | New pending |

## ProcessingStatus

Used by `orders.processing_status` (internal lifecycle).

| Value | Name | Meaning |
|---:|---|---|
| 1 | OrderPlaced | Local order row created |
| 10 | TradesSyncPending | Waiting for trade sync |
| 11 | TradesSyncInProgress | Trade sync running |
| 12 | TradesSynced | Trade sync complete |
| 13 | TradesSyncFailed | Trade sync failed |
| 20 | PositionUpdatePending | Position update queued |
| 21 | PositionUpdating | Position update running |
| 22 | PositionUpdated | Position update complete |
| 23 | PositionUpdateFailed | Position update failed |
| 100 | Completed | Workflow completed |

## TradingMode

Used by decision pipeline/reporting fields.

| Value | Name | Meaning |
|---:|---|---|
| 0 | Spot | Spot trading mode |
| 1 | Futures | Futures trading mode |

## TradeExecutionIntent

Used in decision pipeline/reporting fields.

| Value | Name | Meaning |
|---:|---|---|
| 0 | None | No execution intent |
| 1 | OpenLong | Open long |
| 2 | CloseLong | Close long |
| 3 | OpenShort | Open short |
| 4 | CloseShort | Close short |

## DecisionStatus

Used by `trade_execution_decisions.decisionstatus`.

| Value | Name | Meaning |
|---:|---|---|
| 0 | Pending | Decision recorded, not finalized |
| 1 | Skipped | Blocked before execution |
| 2 | Executed | Execution path completed |
| 3 | Failed | Execution attempted but failed |

## GuardStage

Used by `trade_execution_decisions.guardstage`.

| Value | Name | Meaning |
|---:|---|---|
| 0 | None | No guard stage recorded |
| 1 | Cooldown | Blocked by cooldown |
| 2 | Idempotency | Blocked as duplicate/idempotent |
| 3 | PositionGuard | Blocked by position-aware guard |
| 4 | Risk | Blocked by risk validation |
| 5 | FeeProfitGuard | Blocked by fee/profit gate |
| 6 | ConfidenceGate | Blocked by confidence threshold |
| 7 | Execution | Reached execution stage |
| 8 | UnsupportedMode | Blocked due to unsupported mode |

## OrderSource

Used by `orders.order_source`.

| Value | Name | Meaning |
|---:|---|---|
| 0 | Unknown | Source not specified |
| 1 | DecisionWorker | Decision-based order |
| 2 | TradeMonitorWorker | Protective/monitor-driven order |
| 3 | PositionReconciliationWorker | Reconciliation-driven order |
| 4 | Manual | Manual internal action |
| 5 | Api | API-triggered action |

## CloseReason

Used by `orders.close_reason`.

| Value | Name | Meaning |
|---:|---|---|
| 0 | None | Not a close order reason |
| 1 | StopLoss | Closed due to stop loss |
| 2 | TakeProfit | Closed due to take profit |
| 3 | MaxDuration | Closed due to max duration/time exit |
| 4 | ManualClose | Closed manually |
| 5 | Reconciliation | Closed during reconciliation |
| 6 | OppositeSignal | Closed due to opposite signal |
| 7 | RiskExit | Closed due to risk-based exit |
| 99 | Unknown | Unknown/unmapped close reason |

## PositionExitReason

Used by `positions.exit_reason`.

| Value | Name | Meaning |
|---:|---|---|
| 1 | StopLoss | Position hit stop loss |
| 2 | TakeProfit | Position hit take profit |
| 3 | Time | Position closed by time rule |
| 4 | TrailingStop | Position closed by trailing stop |

## PositionType

Present in project enums (not currently persisted in the four main tables above).

| Value | Name | Meaning |
|---:|---|---|
| 0 | None | No position type |
| 1 | Long | Long type |
| 2 | Short | Short type |

