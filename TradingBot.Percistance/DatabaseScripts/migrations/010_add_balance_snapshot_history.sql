CREATE TABLE IF NOT EXISTS public.balance_snapshot_history (
    id int8 GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset text NOT NULL,
    asset_id int4 NULL,
    free numeric(38, 18) NOT NULL,
    locked numeric(38, 18) NOT NULL,
    total numeric(38, 18) GENERATED ALWAYS AS (free + locked) STORED,
    source varchar(50) NOT NULL DEFAULT 'BinanceAccount',
    sync_correlation_id varchar(40) NULL,
    captured_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_balance_snapshot_history_asset_captured_at_desc
    ON public.balance_snapshot_history (asset, captured_at DESC);

CREATE INDEX IF NOT EXISTS ix_balance_snapshot_history_captured_at_desc
    ON public.balance_snapshot_history (captured_at DESC);

CREATE INDEX IF NOT EXISTS ix_balance_snapshot_history_sync_correlation_id
    ON public.balance_snapshot_history (sync_correlation_id);
