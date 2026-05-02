ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS order_source integer NOT NULL DEFAULT 0;

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS close_reason integer NOT NULL DEFAULT 0;

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS parent_position_id bigint NULL;

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS correlationid varchar(40) NULL;

COMMENT ON COLUMN orders.order_source IS 'Who created the order (DecisionWorker, TradeMonitorWorker, Api, Manual, etc).';
COMMENT ON COLUMN orders.close_reason IS 'Reason for close-style orders (StopLoss, TakeProfit, MaxDuration, ManualClose, etc).';
COMMENT ON COLUMN orders.parent_position_id IS 'Position id that triggered this order (when applicable).';
COMMENT ON COLUMN orders.correlationid IS 'Correlation id for tracing execution pipeline.';
