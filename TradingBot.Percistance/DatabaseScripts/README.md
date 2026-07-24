# Spot/Futures database setup

For a new PostgreSQL database, run these scripts in order:

1. `orders.sql`
2. `positions.sql`
3. `trade_executions.sql`
4. `trade_execution_decisions.sql`
5. `migrations/013_add_spot_futures_cross_market_evaluations.sql`
6. `migrations/014_add_adaptive_rolling_profit_exit_v1.sql`

The other retained migrations are upgrade scripts for databases created by an older branch.
Migration `012_add_testnet_execution_isolation.sql` is safe to run against an existing
database and adds the execution-environment discriminator used by this feature.
