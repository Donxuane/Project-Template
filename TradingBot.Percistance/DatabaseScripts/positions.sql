CREATE TABLE IF NOT EXISTS positions
(
    id             uuid        PRIMARY KEY,
    symbol         text        NOT NULL,
    side           text        NOT NULL,
    quantity       numeric(38, 18) NOT NULL,
    average_price  numeric(38, 18) NOT NULL,
    realized_pnl   numeric(38, 18) NOT NULL DEFAULT 0,
    unrealized_pnl numeric(38, 18) NOT NULL DEFAULT 0,
    is_open        boolean     NOT NULL DEFAULT true,
    created_at     timestamptz NOT NULL,
    updated_at     timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_positions_symbol_is_open
    ON positions (symbol, is_open);

