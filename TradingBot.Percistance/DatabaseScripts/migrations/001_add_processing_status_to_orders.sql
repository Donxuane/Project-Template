-- Add ProcessingStatus column to orders (internal workflow state). Default 1 = OrderPlaced.
ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS processing_status integer NOT NULL DEFAULT 1;

-- Index for worker queries: TradeSyncWorker (TradesSyncPending), PositionWorker (TradesSynced), etc.
CREATE INDEX IF NOT EXISTS ix_orders_processing_status
    ON orders (processing_status);

COMMENT ON COLUMN orders.processing_status IS 'Internal workflow state: 1=OrderPlaced, 10=TradesSyncPending, 11=TradesSyncInProgress, 12=TradesSynced, 13=TradesSyncFailed, 20=PositionUpdatePending, 21=PositionUpdating, 22=PositionUpdated, 23=PositionUpdateFailed, 100=Completed';
