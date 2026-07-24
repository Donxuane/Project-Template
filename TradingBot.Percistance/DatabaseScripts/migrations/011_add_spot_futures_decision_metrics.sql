ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS expectedmovepercent numeric NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS trendconfidencescore integer NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS shortmaslopepercent numeric NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS trendstrengthpercent numeric NULL;

