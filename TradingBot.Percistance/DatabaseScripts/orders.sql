CREATE TABLE IF NOT EXISTS orders
(
    id                uuid        PRIMARY KEY,
    exchange_order_id bigint      UNIQUE,
    symbol            text        NOT NULL,
    side              text        NOT NULL,
    status            text        NOT NULL,
    price             numeric(38, 18) NOT NULL,
    quantity          numeric(38, 18) NOT NULL,
    created_at        timestamptz NOT NULL,
    updated_at        timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_orders_symbol_status_created_at
    ON orders (symbol, status, created_at);

CREATE INDEX IF NOT EXISTS ix_orders_exchange_order_id
    ON orders (exchange_order_id);

