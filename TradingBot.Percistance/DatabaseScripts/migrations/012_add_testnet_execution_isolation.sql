-- 012_add_testnet_execution_isolation.sql
--
-- Purpose: Isolate ETH15 Binance Futures Testnet execution rows from the live Spot
--          pipeline using a single additive, nullable discriminator column per table.
--
-- Why required:
--   The live Spot sync/monitor/reconciliation workers and analytics read the shared
--   orders/positions tables with no venue filter. Without an explicit marker they would
--   attempt to manage a testnet futures short via Spot endpoints and would pollute live
--   metrics. A nullable column (NULL = legacy live-spot) leaves every existing row and
--   query untouched while letting the testnet module and the live workers filter cleanly.
--
-- Why existing structures are insufficient:
--   orders.order_source records WHO created an order, not WHICH venue/environment it runs
--   against; orders.correlationid is a 40-char trace id. positions has no source/venue/mode
--   column at all. trade_executions does NOT need a column: testnet fills are reachable via
--   order_id -> orders.execution_environment. Trading mode is recorded in the existing
--   trade_execution_decisions.tradingmode column; leverage (1x) and notional (10 USDT) are
--   constants captured in the decision row and the JSON/CSV reports.
--
-- Safety: additive, nullable, idempotent. No data backfill. No table drops. No real trading.

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS execution_environment varchar(24) NULL;

ALTER TABLE positions
    ADD COLUMN IF NOT EXISTS execution_environment varchar(24) NULL;

-- Fast testnet-only reads; partial so the live-spot (NULL) hot path is unaffected.
CREATE INDEX IF NOT EXISTS ix_orders_execution_environment
    ON orders (execution_environment)
    WHERE execution_environment IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_positions_execution_environment
    ON positions (execution_environment)
    WHERE execution_environment IS NOT NULL;

COMMENT ON COLUMN orders.execution_environment IS 'Execution venue/environment. NULL = live Binance Spot (legacy). "BinanceFuturesTestnet" = ETH15 testnet-validation orders that the live Spot workers must ignore.';
COMMENT ON COLUMN positions.execution_environment IS 'Execution venue/environment. NULL = live Binance Spot (legacy). "BinanceFuturesTestnet" = ETH15 testnet-validation positions excluded from live monitoring/reconciliation/analytics.';
