-- 013_add_spot_futures_cross_market_evaluations.sql
--
-- Purpose: Persist every per-closed-candle evaluation of the SpotFuturesCrossMarketTestnetV1
--          runtime strategy (synchronized Spot + USD-M Futures indicator snapshots, the
--          cross-market context, and the decided action), including NoTrade cycles.
--
-- Why required:
--   The strategy decides OpenLong/OpenShort/CloseLong/CloseShort/NoTrade on each fully
--   closed candle from two market feeds. trade_execution_decisions captures the decision
--   audit for executed/blocked intents, but there is no store for the full dual-market
--   indicator state per candle. This table makes the whole strategy state reconstructable
--   from the database, mirroring what the Spot pipeline snapshots per decision.
--
-- Orders/positions/executions reuse the existing tables with
-- execution_environment = 'SpotFuturesXTestnetV1' (fits the existing varchar(24)).
--
-- Safety: additive, idempotent. No changes to existing tables.

CREATE TABLE IF NOT EXISTS spot_futures_cross_market_evaluations
(
    id                              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    correlationid                   varchar(40)     NOT NULL,
    symbol                          integer         NOT NULL,
    interval                        varchar(8)      NOT NULL,
    candle_open_time                timestamptz     NOT NULL,
    candle_close_time               timestamptz     NOT NULL,

    -- Spot market state (leading context).
    spot_close                      numeric(38,18)  NOT NULL,
    spot_trend_state                integer         NOT NULL,
    spot_trend_confidence_score     integer         NOT NULL,
    spot_short_ma_slope_percent     numeric(38,18)  NOT NULL,
    spot_trend_strength_percent     numeric(38,18)  NOT NULL,
    spot_momentum_percent           numeric(38,18)  NOT NULL,

    -- Futures market state (confirmation + traded instrument).
    futures_close                   numeric(38,18)  NOT NULL,
    futures_trend_state             integer         NOT NULL,
    futures_trend_confidence_score  integer         NOT NULL,
    futures_short_ma_slope_percent  numeric(38,18)  NOT NULL,
    futures_trend_strength_percent  numeric(38,18)  NOT NULL,
    futures_atr_percent             numeric(38,18)  NOT NULL,

    -- Cross-market context.
    basis_percent                   numeric(38,18)  NOT NULL,
    funding_rate                    numeric(38,18)  NULL,
    mark_price                      numeric(38,18)  NULL,
    markets_in_sync                 boolean         NOT NULL,

    -- Decision output.
    decided_intent                  integer         NOT NULL,
    decision_label                  varchar(16)     NOT NULL,
    reason                          text            NOT NULL DEFAULT '',
    executed                        boolean         NOT NULL DEFAULT false,
    position_id                     bigint          NULL,
    local_order_id                  bigint          NULL,

    created_at                      timestamptz     NOT NULL DEFAULT now()
);

-- One evaluation per symbol/interval/candle (restart-safe dedup).
CREATE UNIQUE INDEX IF NOT EXISTS uq_sfxm_eval_symbol_interval_candle
    ON spot_futures_cross_market_evaluations (symbol, interval, candle_open_time);

CREATE INDEX IF NOT EXISTS ix_sfxm_eval_symbol_created
    ON spot_futures_cross_market_evaluations (symbol, created_at DESC);

COMMENT ON TABLE spot_futures_cross_market_evaluations IS
    'Per-closed-candle evaluation state of the SpotFuturesCrossMarketTestnetV1 strategy (Spot context + Futures confirmation + decision). Includes NoTrade cycles.';
