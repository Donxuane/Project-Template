CREATE TABLE IF NOT EXISTS orders
(
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    exchange_order_id bigint      UNIQUE,
    correlationid     varchar(40) NULL,
    parent_position_id bigint     NULL,
    order_source      integer     NOT NULL DEFAULT 0,
    close_reason      integer     NOT NULL DEFAULT 0,
    symbol            integer    NOT NULL,
    side              integer    NOT NULL,
    status            integer    NOT NULL,
    processing_status integer    NOT NULL DEFAULT 1,
    sync_retry_count  integer    NOT NULL DEFAULT 0,
    price             numeric(38, 18) NOT NULL,
    quantity          numeric(38, 18) NOT NULL,
    created_at        timestamptz NOT NULL,
    updated_at        timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_orders_symbol_status_created_at
    ON orders (symbol, status, created_at);

CREATE INDEX IF NOT EXISTS ix_orders_exchange_order_id
    ON orders (exchange_order_id);

CREATE INDEX IF NOT EXISTS ix_orders_processing_status
    ON orders (processing_status);

