CREATE TABLE IF NOT EXISTS trade_executions
(
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id          bigint      NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    exchange_order_id bigint,
    exchange_trade_id bigint      UNIQUE,
    symbol            integer    NOT NULL,
    side              integer    NOT NULL,
    price             numeric(38, 18) NOT NULL,
    quantity          numeric(38, 18) NOT NULL,
    quote_quantity    numeric(38, 18) NOT NULL DEFAULT 0,
    fee               numeric(38, 18) NOT NULL DEFAULT 0,
    fee_asset         varchar(20),
    position_processed_at timestamptz NULL,
    executed_at       timestamptz NOT NULL,
    created_at        timestamptz NOT NULL,
    updated_at        timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_trade_executions_symbol_executed_at
    ON trade_executions (symbol, executed_at);

CREATE INDEX IF NOT EXISTS ix_trade_executions_order_id
    ON trade_executions (order_id);

