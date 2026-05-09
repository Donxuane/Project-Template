-- 007_repair_historical_position_accounting.sql
-- Historical repair script for legacy position/order/execution inconsistencies.
-- IMPORTANT:
-- - Review diagnostics first (position_accounting_audit.sql).
-- - This script is intentionally conservative.
-- - Run manually; do not auto-run.

BEGIN;

-- =========================================================
-- 0) Safety: ensure durable close-lock column exists
-- =========================================================
ALTER TABLE public.positions
    ADD COLUMN IF NOT EXISTS is_closing bool NOT NULL DEFAULT false;

-- =========================================================
-- 1) Backup candidate rows before any updates
--    NOTE: Fixed backup table name for repeatability.
-- =========================================================
CREATE TABLE IF NOT EXISTS public.positions_repair_backup_20260503 AS
SELECT p.*
FROM public.positions p
WHERE p.id IN (
    SELECT p1.id
    FROM public.positions p1
    WHERE (p1.is_open = FALSE AND p1.quantity <> 0)
       OR (p1.is_open = FALSE AND p1.closed_at IS NULL)
       OR (p1.is_open = FALSE AND p1.exit_price IS NULL)
       OR (p1.is_open = FALSE AND p1.exit_reason IS NULL)
       OR (p1.is_open = FALSE AND p1.is_closing = TRUE)
    UNION
    SELECT DISTINCT o.parent_position_id
    FROM public.orders o
    JOIN public.trade_executions te ON te.order_id = o.id
    WHERE o.parent_position_id IS NOT NULL
      AND o.side = 1
      AND o.close_reason <> 0
);

-- =========================================================
-- 2) A) Backfill positions.exit_reason from orders.close_reason
--    Mapping:
--      1->1, 2->2, 3->3, 4->5, 5->7, 6->6, 7->8, 99->99, 0->NULL (skip)
--    Uses latest close order per parent_position_id.
-- =========================================================
WITH latest_close_order AS (
    SELECT DISTINCT ON (o.parent_position_id)
        o.parent_position_id AS position_id,
        o.close_reason
    FROM public.orders o
    WHERE o.parent_position_id IS NOT NULL
      AND o.side = 1
      AND o.close_reason <> 0
    ORDER BY o.parent_position_id, o.created_at DESC, o.id DESC
),
mapped AS (
    SELECT
        lco.position_id,
        CASE lco.close_reason
            WHEN 1 THEN 1   -- StopLoss
            WHEN 2 THEN 2   -- TakeProfit
            WHEN 3 THEN 3   -- MaxDuration -> Time
            WHEN 4 THEN 5   -- ManualClose
            WHEN 5 THEN 7   -- Reconciliation
            WHEN 6 THEN 6   -- OppositeSignal
            WHEN 7 THEN 8   -- RiskExit
            WHEN 99 THEN 99 -- Unknown
            ELSE NULL
        END AS mapped_exit_reason
    FROM latest_close_order lco
)
UPDATE public.positions p
SET
    exit_reason = m.mapped_exit_reason,
    updated_at = NOW()
FROM mapped m
WHERE p.id = m.position_id
  AND p.exit_reason IS NULL
  AND m.mapped_exit_reason IS NOT NULL;

-- =========================================================
-- 3) B) For closed positions, set quantity = 0
--    ONLY when there is evidence of a linked SELL close execution.
-- =========================================================
UPDATE public.positions p
SET
    quantity = 0,
    updated_at = NOW()
WHERE p.is_open = FALSE
  AND p.quantity <> 0
  AND EXISTS (
      SELECT 1
      FROM public.orders o
      JOIN public.trade_executions te ON te.order_id = o.id
      WHERE o.parent_position_id = p.id
        AND o.side = 1          -- SELL
        AND o.close_reason <> 0 -- close order
        AND te.side = 1         -- SELL fill
  );

-- =========================================================
-- 4) C) Recalculate exit_price from latest linked SELL fill
--    Uses trade_executions.price only (never orders.price).
-- =========================================================
WITH latest_sell_fill AS (
    SELECT DISTINCT ON (o.parent_position_id)
        o.parent_position_id AS position_id,
        te.price AS sell_fill_price
    FROM public.orders o
    JOIN public.trade_executions te ON te.order_id = o.id
    WHERE o.parent_position_id IS NOT NULL
      AND o.side = 1
      AND o.close_reason <> 0
      AND te.side = 1
    ORDER BY o.parent_position_id, te.executed_at DESC, te.id DESC
)
UPDATE public.positions p
SET
    exit_price = lsf.sell_fill_price,
    updated_at = NOW()
FROM latest_sell_fill lsf
WHERE p.id = lsf.position_id
  AND p.is_open = FALSE
  AND p.exit_price IS DISTINCT FROM lsf.sell_fill_price;

-- =========================================================
-- 5) D) Recalculate realized_pnl for simple one-BUY/one-SELL histories only
--    Formula:
--      (sell_price - buy_price) * min(buy_qty, sell_qty) - sell_quote_fee
--    Fees:
--      fee deducted only when fee_asset = 'USDT'
--    Complex histories are intentionally skipped.
-- =========================================================
WITH per_position_exec AS (
    SELECT
        o.parent_position_id AS position_id,
        COUNT(*) FILTER (WHERE te.side = 0) AS buy_exec_count,
        COUNT(*) FILTER (WHERE te.side = 1) AS sell_exec_count,
        SUM(CASE WHEN te.side = 0 THEN te.quantity ELSE 0 END) AS buy_qty,
        SUM(CASE WHEN te.side = 1 THEN te.quantity ELSE 0 END) AS sell_qty,
        SUM(CASE WHEN te.side = 0 THEN te.price * te.quantity ELSE 0 END) AS buy_notional,
        SUM(CASE WHEN te.side = 1 THEN te.price * te.quantity ELSE 0 END) AS sell_notional,
        SUM(CASE WHEN te.side = 1 AND te.fee_asset = 'USDT' THEN te.fee ELSE 0 END) AS sell_quote_fee
    FROM public.orders o
    JOIN public.trade_executions te ON te.order_id = o.id
    WHERE o.parent_position_id IS NOT NULL
    GROUP BY o.parent_position_id
),
simple_recalc AS (
    SELECT
        p.id AS position_id,
        (
            ((pe.sell_notional / NULLIF(pe.sell_qty, 0)) - (pe.buy_notional / NULLIF(pe.buy_qty, 0)))
            * LEAST(pe.buy_qty, pe.sell_qty)
            - pe.sell_quote_fee
        ) AS recomputed_realized_pnl
    FROM public.positions p
    JOIN per_position_exec pe ON pe.position_id = p.id
    WHERE p.is_open = FALSE
      AND pe.buy_exec_count = 1
      AND pe.sell_exec_count = 1
      AND pe.buy_qty > 0
      AND pe.sell_qty > 0
)
UPDATE public.positions p
SET
    realized_pnl = sr.recomputed_realized_pnl,
    updated_at = NOW()
FROM simple_recalc sr
WHERE p.id = sr.position_id
  AND p.realized_pnl IS DISTINCT FROM sr.recomputed_realized_pnl;

-- =========================================================
-- 6) E) Backfill/align closed_at from latest linked SELL execution timestamp
-- =========================================================
WITH latest_sell_execution AS (
    SELECT DISTINCT ON (o.parent_position_id)
        o.parent_position_id AS position_id,
        te.executed_at AS latest_sell_executed_at
    FROM public.orders o
    JOIN public.trade_executions te ON te.order_id = o.id
    WHERE o.parent_position_id IS NOT NULL
      AND o.side = 1
      AND o.close_reason <> 0
      AND te.side = 1
    ORDER BY o.parent_position_id, te.executed_at DESC, te.id DESC
)
UPDATE public.positions p
SET
    closed_at = lse.latest_sell_executed_at,
    updated_at = NOW()
FROM latest_sell_execution lse
WHERE p.id = lse.position_id
  AND p.is_open = FALSE
  AND (
        p.closed_at IS NULL
     OR ABS(EXTRACT(EPOCH FROM (p.closed_at - lse.latest_sell_executed_at))) > 1
  );

-- =========================================================
-- 7) F) Ensure closed positions are not left in closing state
-- =========================================================
UPDATE public.positions p
SET
    is_closing = FALSE,
    updated_at = NOW()
WHERE p.is_open = FALSE
  AND p.is_closing = TRUE;

-- =========================================================
-- 8) G) Mark unprocessed executions for already-handled closed parent orders
--    Only when:
--      - order.processing_status = PositionUpdated (22)
--      - order.status = FILLED (2)
--      - order.parent_position_id exists
--      - parent position is closed
--      - execution is currently unprocessed
-- =========================================================
UPDATE public.trade_executions te
SET
    position_processed_at = COALESCE(te.position_processed_at, p.closed_at, o.updated_at, NOW()),
    updated_at = NOW()
FROM public.orders o
JOIN public.positions p ON p.id = o.parent_position_id
WHERE te.order_id = o.id
  AND te.position_processed_at IS NULL
  AND o.parent_position_id IS NOT NULL
  AND o.processing_status = 22 -- PositionUpdated
  AND o.status = 2             -- FILLED
  AND p.is_open = FALSE;

-- =========================================================
-- 9) Post-repair verification queries
--    Expected: zero rows in each result set for repaired categories.
-- =========================================================

-- 9.1 Closed positions with non-zero quantity
SELECT p.id, p.symbol, p.quantity
FROM public.positions p
WHERE p.is_open = FALSE
  AND p.quantity <> 0
ORDER BY p.id;

-- 9.2 Closed positions missing core close fields
SELECT p.id, p.symbol, p.closed_at, p.exit_price
FROM public.positions p
WHERE p.is_open = FALSE
  AND (p.closed_at IS NULL OR p.exit_price IS NULL)
ORDER BY p.id;

-- 9.3 PositionUpdated orders with unprocessed executions
SELECT
    o.id AS order_id,
    COUNT(*) FILTER (WHERE te.position_processed_at IS NULL) AS unprocessed_exec_count
FROM public.orders o
JOIN public.trade_executions te ON te.order_id = o.id
WHERE o.processing_status = 22
GROUP BY o.id
HAVING COUNT(*) FILTER (WHERE te.position_processed_at IS NULL) > 0
ORDER BY o.id;

-- 9.4 Closed positions left with is_closing = true
SELECT p.id, p.symbol, p.is_open, p.is_closing
FROM public.positions p
WHERE p.is_open = FALSE
  AND p.is_closing = TRUE
ORDER BY p.id;

-- =========================================================
-- Transaction control (manual)
-- =========================================================
-- COMMIT;
-- ROLLBACK;
