# ETH15 Fixed-Frequency Forward Incubation -> Binance Futures Testnet Execution

This is a **testnet-validation phase, not a production deployment**. Real-money trading is
impossible by construction:

- `AllowRealOrders` is a compile-time `false` and the app **fails to start** if config tries to enable it.
- Startup **fails** if the configured testnet base URL is a mainnet host (`fapi.binance.com`, `api.binance.com`, ...) or if the testnet keys match the live (production) `ApiKey`/`SecretKey`.
- Orders are placed only against the Binance Futures Testnet (`https://testnet.binancefuture.com`, `/fapi/*`) using a dedicated client that never shares the live Spot client/keys.
- Defaults: 10 USDT notional, 1x leverage, max 1 open position, max 3 testnet trades/day, hard stop after 5 consecutive losses.

The frozen profile, activation thresholds, entry thresholds, and strategy logic are **not modified**.
The execution path only **consumes** the research outputs produced by the backtest incubation.

## Order flow (every gate must pass before an order is placed)

1. Activation passed (`ActivatedCheckpointCount > 0`).
2. Exact entry signal present (`CurrentExactEntryPresent == true`).
3. Candidate not `Park`.
4. Forward stress-plus net positive (`NetStressPlus > 0`).
5. All frozen-profile health gates pass and the engine verdict is `TestnetOrderCandidate`.
6. Runtime guards pass: at most 1 open position, under the daily trade cap, and below the consecutive-loss hard stop.

Each blocked attempt, activation/entry state, order intent, fill, and exit is logged and persisted
as a `trade_execution_decisions` row. Reports (JSON + CSV) are written every cycle.

## Configuration (`appsettings.json` -> `Eth15TestnetExecution`)

```json
"Eth15TestnetExecution": {
  "Enabled": false,
  "AllowTestnetOrders": false,
  "AllowRealOrders": false,
  "TestnetBaseUrl": "https://testnet.binancefuture.com",
  "TestnetApiKey": "",
  "TestnetSecretKey": "",
  "NotionalUsdt": 10,
  "Leverage": 1,
  "MaxOpenPositions": 1,
  "DailyMaxTrades": 3,
  "MaxConsecutiveLosses": 5,
  "MaxHoldMinutes": 240,
  "IncubationOutputDirectory": "TradingBot.Backtest/output/fixed-frequency-eth15-forward-incubation-v1",
  "ReportOutputDirectory": "output/eth15-testnet-execution",
  "IntervalSeconds": 300
}
```

Prefer setting `TestnetApiKey` / `TestnetSecretKey` via environment variables
(`Eth15TestnetExecution__TestnetApiKey`, `Eth15TestnetExecution__TestnetSecretKey`).

## Database migration (apply before enabling)

Apply the single additive, nullable migration:

```
TradingBot.Percistance/DatabaseScripts/migrations/012_add_testnet_execution_isolation.sql
```

It adds `orders.execution_environment` and `positions.execution_environment` (both nullable;
`NULL` = live Spot). Testnet rows are tagged `"BinanceFuturesTestnet"` and the live Spot
sync/monitor/reconciliation queries are filtered to `execution_environment IS NULL`, so the live
pipeline never touches testnet rows. No new tables; trading mode is recorded in
`trade_execution_decisions.tradingmode`; testnet fills are reachable via `orders.id`.

## Example run

1. Generate fresh incubation outputs (research only, no orders):

   ```bash
   dotnet run --project TradingBot.Backtest -- --fixed-frequency-eth15-forward-incubation-v1 true
   ```

2. Apply the migration `012_add_testnet_execution_isolation.sql` to PostgreSQL.

3. Create Binance Futures **Testnet** API keys at https://testnet.binancefuture.com and set:

   ```json
   "Eth15TestnetExecution": {
     "Enabled": true,
     "AllowTestnetOrders": true,
     "TestnetApiKey": "<testnet key>",
     "TestnetSecretKey": "<testnet secret>"
   }
   ```

4. Start the host (the startup validator fails fast if anything is unsafe):

   ```bash
   dotnet run --project TradingBot
   ```

5. Inspect reports under `output/eth15-testnet-execution/`:
   - `eth15-testnet-summary.json` - running PnL, win rate, consecutive losses, max drawdown.
   - `eth15-testnet-trade-history.json` / `.csv` - per-trade history.
   - `eth15-testnet-equity-curve.json` / `.csv` - cumulative PnL and drawdown.

   And the persisted activity in PostgreSQL (filtered to testnet):
   - `orders WHERE execution_environment = 'BinanceFuturesTestnet'`
   - `positions WHERE execution_environment = 'BinanceFuturesTestnet'`
   - `trade_executions te JOIN orders o ON o.id = te.order_id WHERE o.execution_environment = 'BinanceFuturesTestnet'`
   - `trade_execution_decisions WHERE strategyname = 'Frozen_ETH_NearExtremeShort_15m_T1.25S0.75_PerfRecentNetPositiveChk24hAct12hLB14d_FixedFrequencyV1'`

To stop all testnet ordering, set `Enabled: false` (or `AllowTestnetOrders: false`).
