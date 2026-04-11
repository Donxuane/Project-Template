-- Ensure one latest balance row per (asset, symbol) so BalanceSync can upsert instead of append.
CREATE UNIQUE INDEX IF NOT EXISTS uq_balance_snapshots_asset_symbol
    ON balance_snapshots (asset, symbol);
