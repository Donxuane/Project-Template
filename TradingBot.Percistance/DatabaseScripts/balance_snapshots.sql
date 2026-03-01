CREATE TABLE IF NOT EXISTS balance_snapshots
(
    id         uuid        PRIMARY KEY,
    asset      text        NOT NULL,
    symbol     text        NOT NULL,
    side       text        NOT NULL,
    free       numeric(38, 18) NOT NULL,
    locked     numeric(38, 18) NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_balance_snapshots_asset_symbol_created_at
    ON balance_snapshots (asset, symbol, created_at DESC);

