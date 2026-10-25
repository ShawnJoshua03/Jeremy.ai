# x402 submission

Jeremy.ai, CalHacks 13, Coinbase x402 track. After a laparoscopic cholecystectomy attempt (`lap_chole`), the backend prices each mistake. The total is free. The itemized report is sold through an x402 payment on Base Sepolia testnet.

Sponsor-track eligibility was not confirmed. Nothing here is a claim that a payment settled.

## Where payment sits

1. The trainee finishes an attempt. Scoring in `backend/scoring.py` is deterministic (`lap_chole_findings`). Claude does not decide which mistakes occurred and does not invent a dollar figure.
2. `backend/cost.py` prices the case: OR minutes, path inefficiency, laparoscopic instruments, an assumed clips-and-trocars line, expected cost of major errors at an assumed 0.01 adverse-event probability, and, when a common-bile-duct error occurred, a complication line. Figures are illustrative. See [COST_DATA.md](COST_DATA.md). `GET /api/v1/sessions/{sid}/cost` is this case total. It is not behind payment. It is implemented.
3. The per-mistake report, **designed and not verified**, splits that pricing across the findings on the attempt (injuries, common-bile-duct clip or cut, unsafe division, division before the critical view, unsafe ductotomy, and the minor port, pressure, crossing-clip, and cholangiogram findings). Each line is OR time, adverse-event risk, or complication cost already computed by `cost.py`. The response also carries a total.
4. `GET /api/v1/sessions/{sid}/mistake-cost/summary` is free and returns the total only.
5. `GET /api/v1/sessions/{sid}/mistake-cost/report` is the paid resource.
   - No valid `X-PAYMENT` header: **HTTP 402** with x402 payment requirements. Scheme `exact`, network `base-sepolia`, asset USDC, a small price (the design uses about $0.01), `payTo` from `X402_PAY_TO`.
   - With a header: the server asks the facilitator at `X402_FACILITATOR_URL` to verify, then returns the full report and an `X-PAYMENT-RESPONSE` header.
6. Claude writes one or two plain sentences per mistake about numbers already in the report. Output that contains a dollar figure outside that computed set is discarded and a template is used. That check is specified. It has not been run.

The free summary is enough to see the scale of the attempt. The paid report is the breakdown and the explanation. Payment is in the flow of the detailed report. It is not a paywall on scoring, on the session, or on the case-cost endpoint.

## Testnet only

The designed network is **Base Sepolia**. The asset is testnet USDC. The price is a demo amount, about $0.01. `X402_PAY_TO` is an environment variable, not a committed address. There is no mainnet network, no production merchant account, and no claim of a real charge. A funded testnet wallet has not been used in this docs pass.

Designed environment variables, absent from `.env.example` in this worktree (empty values when they are added):

| Variable | Role |
|---|---|
| `X402_PAY_TO` | Address that receives the testnet payment |
| `X402_FACILITATOR_URL` | Facilitator used to verify `X-PAYMENT` |
| `APIFY_TOKEN` | Apify API token for the price refresh |
| `APIFY_PRICING_ACTOR` | Actor id that refreshes supply prices |

## What is real and what is mocked

| Piece | State in this worktree |
|---|---|
| Deterministic lap-chole findings and practice score | Implemented. Covered by `backend/tests/test_lap_chole.py`. Pytest was not re-run in this docs pass |
| `GET /api/v1/sessions/{id}/cost` | Implemented. A local `fake_quest.py --exercise lap_chole` replay on 2026-10-24 is recorded in [API.md](API.md): illustrative totals $285.16 (one minor error) and $20,188.30 (bile-duct case, assumed complication line). This docs pass did not re-run it |
| Hem-o-lok clips and trocars | Still `ASSUMED_LAP_CHOLE_CLIPS_TROCARS_COST` = $150 in `backend/cost.py`, `assumed: true`, no source URL |
| `backend/data/supply_prices.json` and `pricing.refresh_with_apify()` | **Not in this worktree. Not verified.** Design: each snapshot entry is `item`, `usd`, `source_url`, `retrieved_at`. Apify refreshes it. Offline or a failed call keeps the snapshot. The `assumed` flag stays true only when no sourced price exists. Seed rows only when a citation exists |
| `GET .../mistake-cost/summary` and `GET .../mistake-cost/report` | **Not in this worktree. Not verified** |
| HTTP 402, `X-PAYMENT`, facilitator verify, `X-PAYMENT-RESPONSE` | **Designed. Not verified.** Tests are specified to mock the facilitator so they need no network |
| Settlement with a funded Base Sepolia wallet | **Not verified** |
| Claude explanation of the per-mistake numbers | **Designed. Not verified.** No live Claude check has been run (see [STATUS.md](STATUS.md)); `POST /coach` is a different route |
| Apify actor run | **Not verified** |

Modules named by the design and absent here: `backend/pricing.py`, `backend/x402_gate.py`, `backend/mistake_cost_router.py`.

## Honesty

Every total is an illustrative estimate, not a bill. The 0.01 adverse-event probability per error is an assumption. Applying the published general-surgery complication cost to a simulated bile-duct injury is an assumption. The paid report, when it exists, has to show those flags. It must not let the model replace a computed number.
