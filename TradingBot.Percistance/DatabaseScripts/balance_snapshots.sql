CREATE TABLE IF NOT EXISTS balance_snapshots
(
    id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset      text            NOT NULL,
    symbol     integer         NOT NULL,
    side       integer         NOT NULL,
    free       numeric(38, 18) NOT NULL,
    locked     numeric(38, 18) NOT NULL,
    created_at timestamptz     NOT NULL,
    updated_at timestamptz     NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_balance_snapshots_asset_symbol_created_at
    ON balance_snapshots (asset, symbol, created_at DESC);

