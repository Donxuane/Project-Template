ALTER TABLE positions
    ADD COLUMN IF NOT EXISTS stop_loss_price numeric(38, 18),
    ADD COLUMN IF NOT EXISTS take_profit_price numeric(38, 18),
    ADD COLUMN IF NOT EXISTS exit_price numeric(38, 18),
    ADD COLUMN IF NOT EXISTS opened_at timestamptz,
    ADD COLUMN IF NOT EXISTS closed_at timestamptz;
