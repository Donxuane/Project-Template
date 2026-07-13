CREATE TABLE IF NOT EXISTS futures_commission_rates
(
    id                      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    application_id          varchar(80)     NOT NULL,
    execution_environment   varchar(24)     NOT NULL,
    account_key             varchar(120)    NOT NULL,
    symbol                  integer         NOT NULL,
    maker_commission_rate   numeric(38,18)  NOT NULL,
    taker_commission_rate   numeric(38,18)  NOT NULL,
    rpi_commission_rate     numeric(38,18)  NULL,
    source                  varchar(80)     NOT NULL,
    is_fallback             boolean         NOT NULL DEFAULT false,
    fallback_reason         text            NULL,
    fetched_at              timestamptz     NOT NULL,
    created_at              timestamptz     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_futures_commission_rates_scope
    ON futures_commission_rates (application_id, execution_environment, account_key, symbol, fetched_at DESC);

CREATE TABLE IF NOT EXISTS adaptive_rolling_profit_exit_states
(
    position_id                         bigint PRIMARY KEY REFERENCES positions(id) ON DELETE CASCADE,
    symbol                              integer         NOT NULL,
    side                                integer         NOT NULL,
    state                               integer         NOT NULL,
    remaining_quantity                  numeric(38,18)  NOT NULL,
    entry_price                         numeric(38,18)  NOT NULL,
    entry_notional                      numeric(38,18)  NOT NULL,
    original_stop_loss_price            numeric(38,18)  NULL,
    original_take_profit_price          numeric(38,18)  NULL,
    current_stop_loss_price             numeric(38,18)  NULL,
    current_take_profit_price           numeric(38,18)  NULL,
    original_max_hold_until             timestamptz     NULL,
    eligible_since                      timestamptz     NULL,
    consecutive_profitable_observations integer         NOT NULL DEFAULT 0,
    armed_at                            timestamptz     NULL,
    arming_executable_price             numeric(38,18)  NULL,
    arming_projected_net_pnl            numeric(38,18)  NULL,
    arming_fee_snapshot_json            jsonb           NULL,
    arming_trend_flow_snapshot_json     jsonb           NULL,
    peak_projected_net_pnl              numeric(38,18)  NOT NULL DEFAULT 0,
    best_executable_price               numeric(38,18)  NULL,
    peak_updated_at                     timestamptz     NULL,
    last_peak_persisted_at              timestamptz     NULL,
    last_projected_net_pnl              numeric(38,18)  NOT NULL DEFAULT 0,
    last_gross_projected_pnl            numeric(38,18)  NOT NULL DEFAULT 0,
    last_estimated_exit_fee             numeric(38,18)  NOT NULL DEFAULT 0,
    last_actual_entry_fee               numeric(38,18)  NOT NULL DEFAULT 0,
    last_funding                        numeric(38,18)  NOT NULL DEFAULT 0,
    last_adverse_move_reserve           numeric(38,18)  NOT NULL DEFAULT 0,
    last_spread_bps                     numeric(38,18)  NOT NULL DEFAULT 0,
    last_estimated_slippage_bps         numeric(38,18)  NOT NULL DEFAULT 0,
    last_trend_flow_score               numeric(38,18)  NOT NULL DEFAULT 0,
    last_fee_source                     varchar(80)     NULL,
    last_fee_age_seconds                bigint          NULL,
    last_decision                       varchar(80)     NULL,
    last_rejection_reason               text            NULL,
    last_evaluated_at                   timestamptz     NULL,
    last_transition_at                  timestamptz     NULL,
    close_correlation_id                varchar(40)     NULL,
    close_local_order_id                bigint          NULL,
    close_exchange_order_id             bigint          NULL,
    close_submitted_at                  timestamptz     NULL,
    close_acknowledged_at               timestamptz     NULL,
    close_filled_at                     timestamptz     NULL,
    actual_realized_gross_pnl           numeric(38,18)  NULL,
    actual_realized_net_pnl             numeric(38,18)  NULL,
    created_at                          timestamptz     NOT NULL DEFAULT now(),
    updated_at                          timestamptz     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_adaptive_rolling_states_state_updated
    ON adaptive_rolling_profit_exit_states (state, updated_at DESC);

CREATE TABLE IF NOT EXISTS adaptive_rolling_profit_exit_evaluations
(
    id                              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    position_id                     bigint          NOT NULL REFERENCES positions(id) ON DELETE CASCADE,
    symbol                          integer         NOT NULL,
    side                            integer         NOT NULL,
    state                           integer         NOT NULL,
    remaining_quantity              numeric(38,18)  NOT NULL,
    estimated_executable_price      numeric(38,18)  NOT NULL,
    gross_projected_pnl             numeric(38,18)  NOT NULL,
    actual_entry_commissions        numeric(38,18)  NOT NULL,
    estimated_exit_commission       numeric(38,18)  NOT NULL,
    funding                         numeric(38,18)  NOT NULL,
    adverse_move_reserve            numeric(38,18)  NOT NULL,
    projected_net_pnl               numeric(38,18)  NOT NULL,
    break_even_executable_price     numeric(38,18)  NOT NULL,
    peak_projected_net_pnl          numeric(38,18)  NOT NULL,
    giveback_amount                 numeric(38,18)  NOT NULL,
    giveback_percent                numeric(38,18)  NOT NULL,
    spread_bps                      numeric(38,18)  NOT NULL,
    estimated_slippage_bps          numeric(38,18)  NOT NULL,
    top_bid_notional                numeric(38,18)  NOT NULL,
    top_ask_notional                numeric(38,18)  NOT NULL,
    order_book_imbalance            numeric(38,18)  NOT NULL,
    microprice                      numeric(38,18)  NOT NULL,
    aggressive_buy_quantity         numeric(38,18)  NOT NULL,
    aggressive_sell_quantity        numeric(38,18)  NOT NULL,
    aggressive_flow_imbalance       numeric(38,18)  NOT NULL,
    normalized_velocity_bps         numeric(38,18)  NOT NULL,
    realized_volatility_bps         numeric(38,18)  NOT NULL,
    trend_flow_score                numeric(38,18)  NOT NULL,
    market_data_event_time          timestamptz     NULL,
    market_data_transaction_time    timestamptz     NULL,
    market_data_local_receipt_time  timestamptz     NULL,
    evaluated_at                    timestamptz     NOT NULL,
    market_data_age_ms              bigint          NOT NULL,
    stream_latency_ms               bigint          NOT NULL,
    is_market_data_fresh            boolean         NOT NULL,
    decision                        varchar(80)     NOT NULL,
    rejection_reason                text            NULL,
    snapshot_json                   jsonb           NOT NULL,
    created_at                      timestamptz     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_adaptive_rolling_evals_position_created
    ON adaptive_rolling_profit_exit_evaluations (position_id, created_at DESC);

CREATE TABLE IF NOT EXISTS adaptive_rolling_profit_counterfactuals
(
    position_id                         bigint PRIMARY KEY REFERENCES positions(id) ON DELETE CASCADE,
    symbol                              integer         NOT NULL,
    side                                integer         NOT NULL,
    entry_price                         numeric(38,18)  NOT NULL,
    quantity                            numeric(38,18)  NOT NULL,
    original_stop_loss_price            numeric(38,18)  NULL,
    original_take_profit_price          numeric(38,18)  NULL,
    original_max_hold_until             timestamptz     NULL,
    actual_rolling_exit_price           numeric(38,18)  NOT NULL,
    actual_rolling_net_pnl              numeric(38,18)  NOT NULL,
    actual_rolling_closed_at            timestamptz     NOT NULL,
    max_additional_favorable_move       numeric(38,18)  NOT NULL DEFAULT 0,
    max_avoided_adverse_move            numeric(38,18)  NOT NULL DEFAULT 0,
    counterfactual_exit_price           numeric(38,18)  NULL,
    counterfactual_net_pnl              numeric(38,18)  NULL,
    counterfactual_exit_reason          varchar(80)     NULL,
    better_exit_method                  varchar(80)     NULL,
    is_active                           boolean         NOT NULL DEFAULT true,
    completed_at                        timestamptz     NULL,
    created_at                          timestamptz     NOT NULL DEFAULT now(),
    updated_at                          timestamptz     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_adaptive_rolling_counterfactuals_active
    ON adaptive_rolling_profit_counterfactuals (is_active, updated_at)
    WHERE is_active = TRUE;
