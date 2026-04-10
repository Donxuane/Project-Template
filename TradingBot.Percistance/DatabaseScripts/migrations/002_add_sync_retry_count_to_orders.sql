-- Track retry count for trade/position sync failures (for max retry limit).
ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS sync_retry_count integer NOT NULL DEFAULT 0;

COMMENT ON COLUMN orders.sync_retry_count IS 'Incremented on TradesSyncFailed; reset on TradesSynced. Used to enforce max retry.';
