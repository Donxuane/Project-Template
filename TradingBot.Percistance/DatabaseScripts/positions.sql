CREATE TABLE IF NOT EXISTS positions
(
    id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    symbol         integer        NOT NULL,
    side           integer        NOT NULL,
    quantity       numeric(38, 18) NOT NULL,
    average_price  numeric(38, 18) NOT NULL,
    stop_loss_price numeric(38, 18),
    take_profit_price numeric(38, 18),
    exit_price     numeric(38, 18),
    exit_reason    integer,
    opened_at      timestamptz,
    closed_at      timestamptz,
    realized_pnl   numeric(38, 18) NOT NULL DEFAULT 0,
    unrealized_pnl numeric(38, 18) NOT NULL DEFAULT 0,
    is_open        boolean        NOT NULL DEFAULT true,
    is_closing     boolean        NOT NULL DEFAULT false,
    created_at     timestamptz    NOT NULL,
    updated_at     timestamptz    NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_positions_symbol_is_open
    ON positions (symbol, is_open);

