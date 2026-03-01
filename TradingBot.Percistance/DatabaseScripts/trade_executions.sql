CREATE TABLE IF NOT EXISTS trade_executions
(
    id                uuid        PRIMARY KEY,
    order_id          uuid        NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    exchange_order_id bigint,
    exchange_trade_id bigint      UNIQUE,
    symbol            text        NOT NULL,
    side              text        NOT NULL,
    price             numeric(38, 18) NOT NULL,
    quantity          numeric(38, 18) NOT NULL,
    executed_at       timestamptz NOT NULL,
    created_at        timestamptz NOT NULL,
    updated_at        timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_trade_executions_symbol_executed_at
    ON trade_executions (symbol, executed_at);

CREATE INDEX IF NOT EXISTS ix_trade_executions_order_id
    ON trade_executions (order_id);

