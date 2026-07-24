-- Adds the discriminator used to isolate Spot/Futures testnet orders and positions.
-- Additive and idempotent for databases created by an older branch.

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS execution_environment varchar(24) NULL;

ALTER TABLE positions
    ADD COLUMN IF NOT EXISTS execution_environment varchar(24) NULL;

CREATE INDEX IF NOT EXISTS ix_orders_execution_environment
    ON orders (execution_environment)
    WHERE execution_environment IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_positions_execution_environment
    ON positions (execution_environment)
    WHERE execution_environment IS NOT NULL;

COMMENT ON COLUMN orders.execution_environment IS
    'Execution environment for an isolated Spot/Futures testnet order.';
COMMENT ON COLUMN positions.execution_environment IS
    'Execution environment for an isolated Spot/Futures testnet position.';
