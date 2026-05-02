# Decision Execution Reporting API Examples

The `decisionExecutionView` endpoint is decision-centric and includes skipped and executed decisions.

- Base route: `GET /api/reportings/decisionExecutionView`
- Pagination uses **zero-based** `pageIndex` (`0` is first page).

## All decisions

`GET /api/reportings/decisionExecutionView?pageSize=50&pageIndex=0`

## Only skipped decisions

`GET /api/reportings/decisionExecutionView?pageSize=50&pageIndex=0&onlySkipped=true`

## Only failed decisions

`GET /api/reportings/decisionExecutionView?pageSize=50&pageIndex=0&onlyFailed=true`

## Only cooldown-blocked decisions

`GET /api/reportings/decisionExecutionView?pageSize=50&pageIndex=0&onlyCooldownBlocked=true`

## Only idempotency duplicates

`GET /api/reportings/decisionExecutionView?pageSize=50&pageIndex=0&onlyIdempotencyDuplicates=true`

## BNBUSDT only

`GET /api/reportings/decisionExecutionView?pageSize=50&pageIndex=0&symbol=3`

## Blocked by confidence gate

`GET /api/reportings/decisionExecutionView?pageSize=50&pageIndex=0&blockedBy=6`
