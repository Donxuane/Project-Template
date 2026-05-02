ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS idempotencykey varchar(64) NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS strategyname varchar(120) NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS rawsignal integer NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS tradingmode integer NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS executionintent integer NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS decisionstatus integer NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS guardstage integer NULL;

COMMENT ON COLUMN trade_execution_decisions.idempotencykey IS 'Deterministic key used by idempotency gate.';
COMMENT ON COLUMN trade_execution_decisions.strategyname IS 'Strategy identifier used when generating decision.';
COMMENT ON COLUMN trade_execution_decisions.rawsignal IS 'Raw strategy signal before execution-intent mapping.';
COMMENT ON COLUMN trade_execution_decisions.tradingmode IS 'Trading mode used for this decision.';
COMMENT ON COLUMN trade_execution_decisions.executionintent IS 'Mapped execution intent for this decision.';
COMMENT ON COLUMN trade_execution_decisions.decisionstatus IS 'Final state of decision (pending/skipped/executed/failed).';
COMMENT ON COLUMN trade_execution_decisions.guardstage IS 'Guard stage that blocked decision or execution stage.';
