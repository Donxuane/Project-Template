ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS expectedmovepercent numeric NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS expectedtargetprice numeric NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS expectedtargetsource text NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS trendconfidencescore integer NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS marketconditionscore integer NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS volatilityregime text NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS requiresreducedpositionsize boolean NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS consecutivebullishtrendcandles integer NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS currentcloseaboverecenthigh boolean NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS distancetoinvalidationpercent numeric NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS previouscandlebearish boolean NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS entrynearrecenthigh boolean NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS shortmaslopepercent numeric NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS trendstrengthpercent numeric NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS projectionmode text NULL;

ALTER TABLE trade_execution_decisions
    ADD COLUMN IF NOT EXISTS projectedextension numeric NULL;
