# TradingBot.Backtest

Isolated console replay harness for historical 1m spot strategy testing.

## Scope

- Replays historical 1m candles from local files.
- Reuses runtime strategy components:
  - `TrendStateService`
  - `VolatilityService`
  - `AtrService`
  - `MarketConditionService`
  - `MovingAverageTrendStrategy`
- Simulates spot long-only behavior:
  - max one open long per symbol
  - buy opens
  - sell closes
  - no add-to-position
- Writes CSV/JSON reports only.

## Safety

- No live workers started.
- No Binance order placement.
- No production DB writes.
- No interaction with order sync/trade sync/position workers/accounting/PnL flows.

## Data files

Place files in `TradingBot.Backtest/data` (or pass `--data-dir`):

- `ETHUSDT-1m.csv` / `ETHUSDT-1m.json`
- `BNBUSDT-1m.csv` / `BNBUSDT-1m.json`
- `SOLUSDT-1m.csv` / `SOLUSDT-1m.json`

CSV columns expected:

`openTime,open,high,low,close,volume[,symbol]`

## Run

```bash
dotnet run --project TradingBot.Backtest -- --data-dir TradingBot.Backtest/data --output-dir TradingBot.Backtest/output/run1
```

Optional bootstrap mode for missing local files:

```bash
dotnet run --project TradingBot.Backtest -- --data-dir TradingBot.Backtest/data --bootstrap true --bootstrap-limit 1000
```

Historical paginated bootstrap windows (1m cache):

```bash
# Last 7 days
dotnet run --project TradingBot.Backtest -- --data-dir TradingBot.Backtest/data --bootstrap true --bootstrap-days 7

# Last 14 days
dotnet run --project TradingBot.Backtest -- --data-dir TradingBot.Backtest/data --bootstrap true --bootstrap-days 14

# Last 30 days
dotnet run --project TradingBot.Backtest -- --data-dir TradingBot.Backtest/data --bootstrap true --bootstrap-days 30

# Custom UTC window
dotnet run --project TradingBot.Backtest -- --data-dir TradingBot.Backtest/data --bootstrap true --bootstrap-start 2026-05-01T00:00:00Z --bootstrap-end 2026-05-31T00:00:00Z
```

Multi-timeframe batch run (uses local 1m data, aggregates to 3m/5m):

```bash
dotnet run --project TradingBot.Backtest -- --data-dir TradingBot.Backtest/data --output-dir TradingBot.Backtest/output/mtf-run1 --intervals 1m,3m,5m
```
