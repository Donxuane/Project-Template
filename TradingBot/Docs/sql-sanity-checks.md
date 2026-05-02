# SQL Sanity Checks (PostgreSQL)

This pack is for **read-only validation** after position lifecycle/PnL hardening and migrations.

- Safe for prod/dev: **SELECT only**
- No `UPDATE`, `DELETE`, `INSERT`, or DDL
- Adjust time windows as needed (examples use recent 24h/7d)

> Enum-backed columns (`decisionstatus`, `guardstage`, `tradingmode`, `executionintent`, `order_source`, `close_reason`, etc.) are stored as integers. Cross-check numeric values with project enums.

---

## 1) Recent decisions overview
Use this to see the latest decision pipeline outcomes and whether execution was blocked or allowed.

**Bad result:** unexpected `decisionstatus/guardstage`, low confidence passing, repeated execution errors.  
**Empty result:** no recent decisions in selected time window.

```sql
SELECT
    ted.created_at,
    ted.decisionid,
    ted.idempotencykey,
    ted.symbol,
    ted.action,
    ted.side,
    ted.tradingmode,
    ted.executionintent,
    ted.decisionstatus,
    ted.guardstage,
    ted.confidence,
    ted.minconfidence,
    ted.executionsuccess,
    ted.executionerror
FROM trade_execution_decisions ted
WHERE ted.created_at >= NOW() - INTERVAL '24 hours'
ORDER BY ted.created_at DESC
LIMIT 200;
```

## 2) Skipped decisions grouped by guard stage (last 24h)
Confirms where most skips happen.

**Bad result:** sudden spike on one guard stage.  
**Empty result:** no skipped decisions in 24h.

```sql
SELECT
    ted.guardstage,
    COUNT(*) AS skipped_count
FROM trade_execution_decisions ted
WHERE ted.created_at >= NOW() - INTERVAL '24 hours'
  AND ted.decisionstatus = 1 -- Skipped
GROUP BY ted.guardstage
ORDER BY skipped_count DESC;
```

## 3) Executed decisions without local order
Executed decisions should normally have `localorderid`.

**Bad result:** any row returned.  
**Empty result:** expected.

```sql
SELECT
    ted.id,
    ted.created_at,
    ted.decisionid,
    ted.symbol,
    ted.decisionstatus,
    ted.guardstage,
    ted.executionsuccess,
    ted.localorderid
FROM trade_execution_decisions ted
WHERE ted.decisionstatus = 2 -- Executed
  AND ted.localorderid IS NULL
ORDER BY ted.created_at DESC;
```

## 4) Skipped/failed decisions that still have orders
Skipped decisions should not produce orders.

**Bad result:** skipped rows with `localorderid` populated.  
**Empty result:** expected.

```sql
SELECT
    ted.id,
    ted.created_at,
    ted.decisionid,
    ted.decisionstatus,
    ted.guardstage,
    ted.localorderid,
    ted.exchangeorderid
FROM trade_execution_decisions ted
WHERE ted.decisionstatus IN (1, 3) -- Skipped, Failed
  AND ted.localorderid IS NOT NULL
ORDER BY ted.created_at DESC;
```

## 5) Duplicate idempotency key check (recent)
Detects repeated idempotency keys and their outcomes.

**Bad result:** repeated keys with multiple successful executions.  
**Empty result:** no duplicates in selected window.

```sql
WITH recent AS (
    SELECT *
    FROM trade_execution_decisions
    WHERE created_at >= NOW() - INTERVAL '7 days'
      AND idempotencykey IS NOT NULL
)
SELECT
    r.idempotencykey,
    COUNT(*) AS hit_count,
    MIN(r.created_at) AS first_seen,
    MAX(r.created_at) AS last_seen,
    STRING_AGG(DISTINCT COALESCE(r.decisionstatus::text, 'null'), ',') AS decision_statuses,
    STRING_AGG(DISTINCT COALESCE(r.executionsuccess::text, 'null'), ',') AS execution_success_values
FROM recent r
GROUP BY r.idempotencykey
HAVING COUNT(*) > 1
ORDER BY hit_count DESC, last_seen DESC;
```

## 6) Negative/invalid open position quantity
Open spot positions should have positive quantity.

**Bad result:** any row returned.  
**Empty result:** expected.

```sql
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.is_open,
    p.opened_at,
    p.closed_at,
    p.updated_at
FROM positions p
WHERE p.quantity < 0
   OR (p.is_open = TRUE AND p.quantity <= 0)
ORDER BY p.updated_at DESC;
```

## 7) Closed positions with remaining quantity
Closed positions should have zero quantity.

**Bad result:** any row returned.  
**Empty result:** expected.

```sql
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.is_open,
    p.closed_at,
    p.updated_at
FROM positions p
WHERE p.is_open = FALSE
  AND p.quantity <> 0
ORDER BY p.updated_at DESC;
```

## 8) Open positions with `closed_at` populated
Open rows should not carry a close timestamp.

**Bad result:** any row returned.  
**Empty result:** expected.

```sql
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.is_open,
    p.opened_at,
    p.closed_at
FROM positions p
WHERE p.is_open = TRUE
  AND p.closed_at IS NOT NULL
ORDER BY p.updated_at DESC;
```

## 9) Closed positions missing `closed_at`
Closed rows should have close timestamp set.

**Bad result:** any row returned.  
**Empty result:** expected.

```sql
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.is_open,
    p.opened_at,
    p.closed_at,
    p.updated_at
FROM positions p
WHERE p.is_open = FALSE
  AND p.closed_at IS NULL
ORDER BY p.updated_at DESC;
```

## 10) Recent close orders missing parent position
Close orders should normally be linked by `parent_position_id`.

**Bad result:** many recent rows returned.  
**Empty result:** expected for healthy linkage.

```sql
SELECT
    o.id,
    o.created_at,
    o.symbol,
    o.side,
    o.order_source,
    o.close_reason,
    o.parent_position_id,
    o.correlationid
FROM orders o
WHERE o.created_at >= NOW() - INTERVAL '7 days'
  AND o.side = 1 -- SELL (check enum mapping)
  AND o.close_reason IS NOT NULL
  AND o.close_reason <> 0 -- None
  AND o.parent_position_id IS NULL
ORDER BY o.created_at DESC;
```

## 11) Trade executions missing accounting fields
Verifies migration usage for `quote_quantity`, `fee`, `fee_asset`, `position_processed_at`.

**Bad result:** unexpected nulls / zero quote quantity.  
**Empty result:** ideal after rollout.

```sql
SELECT
    te.id,
    te.order_id,
    te.exchange_trade_id,
    te.symbol,
    te.side,
    te.price,
    te.quantity,
    te.quote_quantity,
    te.fee,
    te.fee_asset,
    te.position_processed_at,
    te.executed_at
FROM trade_executions te
WHERE te.executed_at >= NOW() - INTERVAL '7 days'
  AND (
        te.quote_quantity IS NULL
     OR te.quote_quantity <= 0
     OR te.fee IS NULL
     OR te.fee_asset IS NULL
     OR te.position_processed_at IS NULL
  )
ORDER BY te.executed_at DESC;
```

## 12) Unprocessed trade executions on completed orders
Finds fills not consumed by position accounting even though order flow completed.

**Bad result:** persistent rows older than normal worker lag.  
**Empty result:** expected in steady state.

```sql
SELECT
    te.id AS trade_execution_id,
    te.order_id,
    te.exchange_trade_id,
    te.executed_at,
    te.position_processed_at,
    o.status AS order_status,
    o.processing_status,
    o.updated_at AS order_updated_at
FROM trade_executions te
JOIN orders o ON o.id = te.order_id
WHERE te.position_processed_at IS NULL
  AND o.status IN (2, 3) -- FILLED / PARTIALLY_FILLED (verify enum)
ORDER BY te.executed_at DESC;
```

## 13) Possible double-processing risk view
Shows processed/unprocessed mix by order and duplicate exchange trade ids.

**Bad result:** high duplicate counts or unexpected processed mix.  
**Empty result:** no suspicious groups.

```sql
SELECT
    te.order_id,
    COUNT(*) AS trade_rows,
    COUNT(*) FILTER (WHERE te.position_processed_at IS NOT NULL) AS processed_rows,
    COUNT(*) FILTER (WHERE te.position_processed_at IS NULL) AS unprocessed_rows,
    COUNT(DISTINCT te.exchange_trade_id) AS distinct_exchange_trades,
    MIN(te.executed_at) AS first_trade_at,
    MAX(te.executed_at) AS last_trade_at
FROM trade_executions te
GROUP BY te.order_id
HAVING COUNT(*) <> COUNT(DISTINCT te.exchange_trade_id)
    OR (COUNT(*) FILTER (WHERE te.position_processed_at IS NULL) > 0
        AND COUNT(*) FILTER (WHERE te.position_processed_at IS NOT NULL) > 0)
ORDER BY last_trade_at DESC;
```

## 14) Recent realized PnL check
Quick visibility for recently closed positions.

**Bad result:** implausible realized PnL or missing exit fields.

```sql
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.average_price,
    p.exit_price,
    p.realized_pnl,
    p.opened_at,
    p.closed_at,
    p.exit_reason
FROM positions p
WHERE p.is_open = FALSE
  AND p.closed_at >= NOW() - INTERVAL '14 days'
ORDER BY p.closed_at DESC
LIMIT 200;
```

## 15) Manual recalculation helper (approximate)
Approximates gross PnL from closed position row.

**Limitation:** original entry quantity snapshot is not stored in `positions`; this approximation uses current row quantity. For exact historic gross PnL validation, add a dedicated snapshot field (e.g., `entry_quantity`) or reconstruct from trade ledger.

```sql
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.average_price,
    p.exit_price,
    p.realized_pnl,
    CASE
        WHEN p.exit_price IS NOT NULL THEN (p.exit_price - p.average_price) * ABS(p.quantity)
        ELSE NULL
    END AS approx_gross_pnl
FROM positions p
WHERE p.is_open = FALSE
  AND p.closed_at >= NOW() - INTERVAL '14 days'
ORDER BY p.closed_at DESC
LIMIT 200;
```

## 16) Orders without traceability
Finds missing source/correlation context.

**Bad result:** rows with unknown source or null correlation id.  
**Empty result:** expected.

```sql
SELECT
    o.id,
    o.created_at,
    o.symbol,
    o.side,
    o.order_source,
    o.close_reason,
    o.parent_position_id,
    o.correlationid
FROM orders o
WHERE o.order_source = 0 -- Unknown
   OR o.correlationid IS NULL
ORDER BY o.created_at DESC;
```

## 17) TradeMonitor close reason coverage
Sanity distribution of close sources/reasons.

**Bad result:** unexpected source/reason pairs or large `Unknown` bucket.

```sql
SELECT
    o.order_source,
    o.close_reason,
    COUNT(*) AS order_count
FROM orders o
WHERE o.created_at >= NOW() - INTERVAL '14 days'
GROUP BY o.order_source, o.close_reason
ORDER BY o.order_source, o.close_reason;
```

## 18) Decision-report join sanity (latest 50)
Mirrors decision-centric reporting join behavior.

**Bad result:** executed rows with missing linkage, unexpected null combinations.

```sql
SELECT
    ted.created_at,
    ted.id AS decision_db_id,
    ted.decisionid,
    ted.decisionstatus,
    ted.guardstage,
    ted.localorderid,
    o.id AS order_id,
    o.exchange_order_id,
    o.order_source,
    o.close_reason,
    o.parent_position_id,
    te.exchange_trade_id,
    te.price AS execution_price,
    te.quantity AS executed_quantity,
    te.fee,
    p.id AS position_id,
    p.realized_pnl,
    p.exit_reason,
    p.is_open
FROM trade_execution_decisions ted
LEFT JOIN orders o ON o.id = ted.localorderid
LEFT JOIN trade_executions te ON te.order_id = o.id
LEFT JOIN positions p ON p.id = o.parent_position_id
ORDER BY ted.created_at DESC
LIMIT 50;
```

## 19) Parent linkage integrity (`orders.parent_position_id`)
Detects foreign-key-like orphan linkage.

**Bad result:** any row returned.  
**Empty result:** expected.

```sql
SELECT
    o.id AS order_id,
    o.created_at,
    o.symbol,
    o.parent_position_id
FROM orders o
LEFT JOIN positions p ON p.id = o.parent_position_id
WHERE o.parent_position_id IS NOT NULL
  AND p.id IS NULL
ORDER BY o.created_at DESC;
```

## 20) Fees by asset (recent)
Useful for validating fee ingestion and asset mix.

**Bad result:** unexpectedly high unknown/null asset share.

```sql
SELECT
    COALESCE(NULLIF(te.fee_asset, ''), 'UNKNOWN') AS fee_asset,
    COUNT(*) AS trade_count,
    SUM(te.fee) AS total_fee,
    AVG(te.fee) AS avg_fee
FROM trade_executions te
WHERE te.executed_at >= NOW() - INTERVAL '14 days'
GROUP BY COALESCE(NULLIF(te.fee_asset, ''), 'UNKNOWN')
ORDER BY total_fee DESC;
```

---

## Reconciliation checks

These checks help validate local `positions` vs latest `balance_snapshots` after reconciliation hardening.

## 21) Latest balance snapshots by asset
Shows latest known snapshot rows and total (`free + locked`).

**Bad result:** very old `updated_at` rows for active assets.

```sql
SELECT
    bs.asset,
    bs.symbol,
    bs.free,
    bs.locked,
    (bs.free + bs.locked) AS total,
    bs.created_at,
    bs.updated_at
FROM balance_snapshots bs
ORDER BY COALESCE(bs.updated_at, bs.created_at) DESC, bs.asset;
```

## 22) Local open positions vs latest balance snapshots
Compares local open quantity against latest snapshot total for matching base asset.

**Bad result:** `abs_difference` above tolerance.

```sql
WITH params AS (
    SELECT 0.00000001::numeric AS tolerance
),
symbol_asset_map AS (
    SELECT * FROM (VALUES
        (1, 'BTC'),
        (2, 'ETH'),
        (3, 'BNB'),
        (4, 'XRP'),
        (5, 'SOL'),
        (6, 'DOGE'),
        (7, 'ADA'),
        (8, 'DOT'),
        (9, 'SHIB')
    ) AS t(symbol, asset)
),
latest_snapshots AS (
    SELECT DISTINCT ON (asset)
           asset,
           free,
           locked,
           (free + locked) AS total,
           created_at,
           updated_at
    FROM balance_snapshots
    ORDER BY asset, COALESCE(updated_at, created_at) DESC
)
SELECT
    p.id AS position_id,
    p.symbol,
    sam.asset AS base_asset,
    p.quantity AS local_open_quantity,
    ls.free AS snapshot_free,
    ls.locked AS snapshot_locked,
    ls.total AS snapshot_total,
    (p.quantity - COALESCE(ls.total, 0)) AS difference,
    ABS(p.quantity - COALESCE(ls.total, 0)) AS abs_difference,
    pr.tolerance
FROM positions p
CROSS JOIN params pr
LEFT JOIN symbol_asset_map sam ON sam.symbol = p.symbol
LEFT JOIN latest_snapshots ls
    ON ls.asset = sam.asset
WHERE p.is_open = TRUE
ORDER BY abs_difference DESC, p.updated_at DESC;
```

## 23) Assets with exchange balance but no local open position
Finds assets with positive snapshot total that do not have a matching open local symbol position.

**Bad result:** rows indicate missing local position representation.
**Empty result:** expected when local and exchange are aligned.

```sql
WITH params AS (
    SELECT 0.00000001::numeric AS tolerance
),
symbol_asset_map AS (
    SELECT * FROM (VALUES
        (1, 'BTC'),
        (2, 'ETH'),
        (3, 'BNB'),
        (4, 'XRP'),
        (5, 'SOL'),
        (6, 'DOGE'),
        (7, 'ADA'),
        (8, 'DOT'),
        (9, 'SHIB')
    ) AS t(symbol, asset)
),
latest_snapshots AS (
    SELECT DISTINCT ON (asset)
           asset,
           free,
           locked,
           (free + locked) AS total,
           created_at,
           updated_at
    FROM balance_snapshots
    ORDER BY asset, COALESCE(updated_at, created_at) DESC
)
SELECT
    ls.asset,
    ls.free,
    ls.locked,
    ls.total,
    ls.updated_at
FROM latest_snapshots ls
CROSS JOIN params pr
WHERE ls.total > pr.tolerance
  AND ls.asset <> 'USDT'
  AND NOT EXISTS (
      SELECT 1
      FROM positions p
      JOIN symbol_asset_map sam ON sam.symbol = p.symbol
      WHERE p.is_open = TRUE
        AND sam.asset = ls.asset
  )
ORDER BY ls.total DESC;
```

## 24) Local open positions with no exchange balance snapshot
Finds open positions whose base asset has no latest snapshot record.

**Bad result:** rows indicate missing balance data.
**Empty result:** expected in healthy snapshot sync.

```sql
WITH latest_snapshots AS (
    SELECT DISTINCT ON (asset)
           asset,
           free,
           locked,
           (free + locked) AS total,
           created_at,
           updated_at
    FROM balance_snapshots
    ORDER BY asset, COALESCE(updated_at, created_at) DESC
)
SELECT
    p.id AS position_id,
    p.symbol,
    sam.asset AS base_asset,
    p.quantity AS local_open_quantity,
    p.updated_at AS position_updated_at
FROM positions p
LEFT JOIN (
    SELECT * FROM (VALUES
        (1, 'BTC'),
        (2, 'ETH'),
        (3, 'BNB'),
        (4, 'XRP'),
        (5, 'SOL'),
        (6, 'DOGE'),
        (7, 'ADA'),
        (8, 'DOT'),
        (9, 'SHIB')
    ) AS t(symbol, asset)
) sam ON sam.symbol = p.symbol
LEFT JOIN latest_snapshots ls
    ON ls.asset = sam.asset
WHERE p.is_open = TRUE
  AND ls.asset IS NULL
ORDER BY p.updated_at DESC;
```

## 25) Quantity mismatch above tolerance
Direct mismatch finder using latest snapshots and explicit tolerance threshold.

**Bad result:** any rows returned.
**Empty result:** expected when quantities reconcile.

```sql
WITH params AS (
    SELECT 0.00000001::numeric AS tolerance
),
symbol_asset_map AS (
    SELECT * FROM (VALUES
        (1, 'BTC'),
        (2, 'ETH'),
        (3, 'BNB'),
        (4, 'XRP'),
        (5, 'SOL'),
        (6, 'DOGE'),
        (7, 'ADA'),
        (8, 'DOT'),
        (9, 'SHIB')
    ) AS t(symbol, asset)
),
latest_snapshots AS (
    SELECT DISTINCT ON (asset)
           asset,
           free,
           locked,
           (free + locked) AS total,
           created_at,
           updated_at
    FROM balance_snapshots
    ORDER BY asset, COALESCE(updated_at, created_at) DESC
)
SELECT
    p.id AS position_id,
    p.symbol,
    sam.asset AS base_asset,
    p.quantity AS local_open_quantity,
    COALESCE(ls.free, 0) AS snapshot_free,
    COALESCE(ls.locked, 0) AS snapshot_locked,
    COALESCE(ls.total, 0) AS snapshot_total,
    (p.quantity - COALESCE(ls.total, 0)) AS difference,
    ABS(p.quantity - COALESCE(ls.total, 0)) AS abs_difference,
    pr.tolerance,
    ls.updated_at AS snapshot_updated_at
FROM positions p
CROSS JOIN params pr
LEFT JOIN symbol_asset_map sam ON sam.symbol = p.symbol
LEFT JOIN latest_snapshots ls
    ON ls.asset = sam.asset
WHERE p.is_open = TRUE
  AND ABS(p.quantity - COALESCE(ls.total, 0)) > pr.tolerance
ORDER BY abs_difference DESC, p.updated_at DESC;
```

---

## Optional run order
1. Linkage/integrity first: 3, 4, 6, 7, 8, 9, 19  
2. Accounting ingestion: 11, 12, 13, 20  
3. Decision observability: 1, 2, 5, 18  
4. PnL sampling: 14, 15  
5. Traceability checks: 10, 16, 17

