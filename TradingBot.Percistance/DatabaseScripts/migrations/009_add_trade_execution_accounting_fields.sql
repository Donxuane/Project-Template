ALTER TABLE trade_executions
    ADD COLUMN IF NOT EXISTS quote_quantity numeric(38, 18) NOT NULL DEFAULT 0;

ALTER TABLE trade_executions
    ADD COLUMN IF NOT EXISTS fee numeric(38, 18) NOT NULL DEFAULT 0;

ALTER TABLE trade_executions
    ADD COLUMN IF NOT EXISTS fee_asset varchar(20) NULL;

ALTER TABLE trade_executions
    ADD COLUMN IF NOT EXISTS position_processed_at timestamptz NULL;

CREATE INDEX IF NOT EXISTS ix_trade_executions_position_processed_at
    ON trade_executions (position_processed_at);

COMMENT ON COLUMN trade_executions.quote_quantity IS 'Executed quote quantity from exchange (price * quantity fallback when not present).';
COMMENT ON COLUMN trade_executions.fee IS 'Execution fee reported by exchange.';
COMMENT ON COLUMN trade_executions.fee_asset IS 'Asset used to pay execution fee.';
COMMENT ON COLUMN trade_executions.position_processed_at IS 'Timestamp when this trade execution was consumed by position accounting.';
