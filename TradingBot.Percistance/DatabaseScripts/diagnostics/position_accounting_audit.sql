-- position_accounting_audit.sql
-- Read-only diagnostics for historical position/order/execution consistency.
-- IMPORTANT: This script performs SELECT queries only.

-- =========================
-- A) Closed positions with quantity <> 0
-- =========================
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.is_open,
    p.closed_at,
    p.exit_price,
    p.exit_reason,
    p.updated_at
FROM public.positions p
WHERE p.is_open = FALSE
  AND p.quantity <> 0
ORDER BY p.updated_at DESC;

-- =========================
-- B) Closed positions missing closed_at
-- =========================
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.is_open,
    p.closed_at,
    p.exit_price,
    p.exit_reason
FROM public.positions p
WHERE p.is_open = FALSE
  AND p.closed_at IS NULL
ORDER BY p.updated_at DESC;

-- =========================
-- C) Closed positions missing exit_price
-- =========================
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.is_open,
    p.closed_at,
    p.exit_price,
    p.exit_reason
FROM public.positions p
WHERE p.is_open = FALSE
  AND p.exit_price IS NULL
ORDER BY p.updated_at DESC;

-- =========================
-- D) Closed positions missing exit_reason
-- =========================
SELECT
    p.id,
    p.symbol,
    p.quantity,
    p.is_open,
    p.closed_at,
    p.exit_price,
    p.exit_reason
FROM public.positions p
WHERE p.is_open = FALSE
  AND p.exit_reason IS NULL
ORDER BY p.updated_at DESC;

-- =========================
-- E) Exit price differs from latest linked SELL execution fill price
-- =========================
WITH latest_sell_fill AS (
    SELECT DISTINCT ON (o.parent_position_id)
        o.parent_position_id AS position_id,
        te.id AS trade_execution_id,
        te.price AS sell_fill_price,
        te.executed_at AS sell_executed_at
    FROM public.orders o
    JOIN public.trade_executions te ON te.order_id = o.id
    WHERE o.parent_position_id IS NOT NULL
      AND o.side = 1 -- SELL
      AND o.close_reason <> 0 -- not None
      AND te.side = 1 -- SELL fill
    ORDER BY o.parent_position_id, te.executed_at DESC, te.id DESC
)
SELECT
    p.id AS position_id,
    p.symbol,
    p.exit_price AS position_exit_price,
    lsf.sell_fill_price AS latest_sell_fill_price,
    lsf.sell_executed_at,
    (p.exit_price - lsf.sell_fill_price) AS exit_price_diff
FROM public.positions p
JOIN latest_sell_fill lsf ON lsf.position_id = p.id
WHERE p.is_open = FALSE
  AND p.exit_price IS NOT NULL
  AND lsf.sell_fill_price IS NOT NULL
  AND p.exit_price IS DISTINCT FROM lsf.sell_fill_price
ORDER BY ABS(p.exit_price - lsf.sell_fill_price) DESC, p.id;

-- =========================
-- F) Realized PnL differs from simple fill-based recomputation
-- NOTE:
-- - This section intentionally handles only simple, unambiguous history:
--   exactly one BUY execution and exactly one SELL execution linked by parent_position_id.
-- - Quote fee deducted only when fee_asset = 'USDT'.
-- - Complex/multi-fill/partial histories are reported separately (see ambiguous section below).
-- =========================
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
simple_candidates AS (
    SELECT
        p.id AS position_id,
        p.symbol,
        p.realized_pnl AS stored_realized_pnl,
        pe.buy_qty,
        pe.sell_qty,
        pe.buy_notional,
        pe.sell_notional,
        pe.sell_quote_fee,
        CASE
            WHEN pe.buy_qty > 0 AND pe.sell_qty > 0 THEN
                ((pe.sell_notional / pe.sell_qty) - (pe.buy_notional / pe.buy_qty))
                * LEAST(pe.buy_qty, pe.sell_qty)
                - pe.sell_quote_fee
            ELSE NULL
        END AS recomputed_realized_pnl
    FROM public.positions p
    JOIN per_position_exec pe ON pe.position_id = p.id
    WHERE p.is_open = FALSE
      AND pe.buy_exec_count = 1
      AND pe.sell_exec_count = 1
)
SELECT
    sc.position_id,
    sc.symbol,
    sc.stored_realized_pnl,
    sc.recomputed_realized_pnl,
    (sc.stored_realized_pnl - sc.recomputed_realized_pnl) AS pnl_diff,
    sc.buy_qty,
    sc.sell_qty,
    sc.sell_quote_fee
FROM simple_candidates sc
WHERE sc.recomputed_realized_pnl IS NOT NULL
  AND sc.stored_realized_pnl IS DISTINCT FROM sc.recomputed_realized_pnl
ORDER BY ABS(sc.stored_realized_pnl - sc.recomputed_realized_pnl) DESC, sc.position_id;

-- Ambiguous histories intentionally skipped from generic PnL repair.
WITH per_position_exec AS (
    SELECT
        o.parent_position_id AS position_id,
        COUNT(*) FILTER (WHERE te.side = 0) AS buy_exec_count,
        COUNT(*) FILTER (WHERE te.side = 1) AS sell_exec_count
    FROM public.orders o
    JOIN public.trade_executions te ON te.order_id = o.id
    WHERE o.parent_position_id IS NOT NULL
    GROUP BY o.parent_position_id
)
SELECT
    p.id AS position_id,
    p.symbol,
    pe.buy_exec_count,
    pe.sell_exec_count,
    'Skipped from generic repair: requires manual review (multi-fill/partial/ambiguous).' AS reason
FROM public.positions p
JOIN per_position_exec pe ON pe.position_id = p.id
WHERE p.is_open = FALSE
  AND NOT (pe.buy_exec_count = 1 AND pe.sell_exec_count = 1)
ORDER BY p.id;

-- =========================
-- G) PositionUpdated orders with unprocessed executions
-- =========================
SELECT
    o.id AS order_id,
    o.symbol,
    o.side,
    o.parent_position_id,
    o.processing_status,
    o.status AS order_status,
    COUNT(*) FILTER (WHERE te.position_processed_at IS NULL) AS unprocessed_exec_count,
    COUNT(*) AS total_exec_count
FROM public.orders o
JOIN public.trade_executions te ON te.order_id = o.id
WHERE o.processing_status = 22 -- PositionUpdated
GROUP BY o.id, o.symbol, o.side, o.parent_position_id, o.processing_status, o.status
HAVING COUNT(*) FILTER (WHERE te.position_processed_at IS NULL) > 0
ORDER BY o.id;

-- =========================
-- H) Orders stuck at TradesSynced with executions present
-- =========================
SELECT
    o.id AS order_id,
    o.symbol,
    o.side,
    o.parent_position_id,
    o.processing_status,
    o.status AS order_status,
    COUNT(te.id) AS exec_count,
    MAX(te.executed_at) AS last_execution_at
FROM public.orders o
JOIN public.trade_executions te ON te.order_id = o.id
WHERE o.processing_status = 12 -- TradesSynced
GROUP BY o.id, o.symbol, o.side, o.parent_position_id, o.processing_status, o.status
ORDER BY o.id;

-- =========================
-- I) SELL close orders with parent_position_id inconsistencies
-- =========================
SELECT
    o.id AS order_id,
    o.symbol,
    o.side,
    o.status AS order_status,
    o.processing_status,
    o.parent_position_id,
    p.id AS position_id,
    p.is_open AS parent_is_open,
    p.quantity AS parent_quantity,
    p.closed_at AS parent_closed_at,
    p.exit_price AS parent_exit_price,
    CASE
        WHEN p.id IS NULL THEN 'Parent position missing'
        WHEN o.status = 2 AND o.processing_status IN (22, 100) AND p.is_open = TRUE THEN 'Close order handled but parent still open'
        WHEN p.is_open = FALSE AND p.quantity <> 0 THEN 'Parent closed with non-zero quantity'
        ELSE 'Other'
    END AS inconsistency_reason
FROM public.orders o
LEFT JOIN public.positions p ON p.id = o.parent_position_id
WHERE o.parent_position_id IS NOT NULL
  AND o.side = 1 -- SELL
  AND o.close_reason <> 0 -- not None
  AND (
        p.id IS NULL
     OR (o.status = 2 AND o.processing_status IN (22, 100) AND p.is_open = TRUE)
     OR (p.is_open = FALSE AND p.quantity <> 0)
  )
ORDER BY o.id;

-- =========================
-- J) Latest analytics row vs recomputed current closed-position summary
-- NOTE:
-- - Uses ordering by ctid DESC for latest row portability when no timestamp/id is guaranteed.
-- - Recomputed summary follows current runtime inclusion rule:
--   is_open = false AND closed_at IS NOT NULL AND exit_price IS NOT NULL.
-- =========================
WITH closed_positions AS (
    SELECT
        p.id,
        p.realized_pnl,
        p.closed_at,
        p.updated_at
    FROM public.positions p
    WHERE p.is_open = FALSE
      AND p.closed_at IS NOT NULL
      AND p.exit_price IS NOT NULL
),
ordered_equity AS (
    SELECT
        cp.*,
        SUM(cp.realized_pnl) OVER (
            ORDER BY COALESCE(cp.closed_at, cp.updated_at), cp.id
            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        ) AS equity
    FROM closed_positions cp
),
drawdown_calc AS (
    SELECT
        oe.*,
        MAX(oe.equity) OVER (
            ORDER BY COALESCE(oe.closed_at, oe.updated_at), oe.id
            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        ) - oe.equity AS drawdown
    FROM ordered_equity oe
),
recomputed AS (
    SELECT
        COALESCE(SUM(cp.realized_pnl), 0) AS totalpnl,
        COALESCE(
            CASE WHEN COUNT(*) = 0 THEN 0
                 ELSE (COUNT(*) FILTER (WHERE cp.realized_pnl > 0)::numeric / COUNT(*)::numeric) * 100
            END, 0) AS winrate,
        COALESCE(AVG(cp.realized_pnl) FILTER (WHERE cp.realized_pnl > 0), 0) AS averagewin,
        COALESCE(ABS(AVG(cp.realized_pnl) FILTER (WHERE cp.realized_pnl < 0)), 0) AS averageloss,
        COUNT(*)::int AS totaltrades,
        COALESCE(MAX(dc.drawdown), 0) AS maxdrawdown
    FROM closed_positions cp
    LEFT JOIN drawdown_calc dc ON dc.id = cp.id
),
latest_analytics AS (
    SELECT
        a.totalpnl,
        a.winrate,
        a.averagewin,
        a.averageloss,
        a.totaltrades,
        a.maxdrawdown
    FROM public.analytics a
    ORDER BY a.ctid DESC
    LIMIT 1
)
SELECT
    la.totalpnl AS analytics_totalpnl,
    r.totalpnl AS recomputed_totalpnl,
    (la.totalpnl - r.totalpnl) AS diff_totalpnl,
    la.winrate AS analytics_winrate,
    r.winrate AS recomputed_winrate,
    (la.winrate - r.winrate) AS diff_winrate,
    la.averagewin AS analytics_averagewin,
    r.averagewin AS recomputed_averagewin,
    (la.averagewin - r.averagewin) AS diff_averagewin,
    la.averageloss AS analytics_averageloss,
    r.averageloss AS recomputed_averageloss,
    (la.averageloss - r.averageloss) AS diff_averageloss,
    la.totaltrades AS analytics_totaltrades,
    r.totaltrades AS recomputed_totaltrades,
    (la.totaltrades - r.totaltrades) AS diff_totaltrades,
    la.maxdrawdown AS analytics_maxdrawdown,
    r.maxdrawdown AS recomputed_maxdrawdown,
    (la.maxdrawdown - r.maxdrawdown) AS diff_maxdrawdown
FROM latest_analytics la
CROSS JOIN recomputed r;
