-- Ensure unique constraints for worker and API safety (idempotent).

-- orders: exchange_order_id unique (already in table def; add index if missing)
CREATE UNIQUE INDEX IF NOT EXISTS uq_orders_exchange_order_id
    ON orders (exchange_order_id) WHERE exchange_order_id IS NOT NULL;

-- trade_executions: exchange_trade_id unique (already in table def; ensure index)
CREATE UNIQUE INDEX IF NOT EXISTS uq_trade_executions_exchange_trade_id
    ON trade_executions (exchange_trade_id);
