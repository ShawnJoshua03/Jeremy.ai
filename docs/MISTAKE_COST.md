# Paid mistake-cost report: laparoscopic cholecystectomy

The `lap_chole` report prices the backend's deterministic findings. Gemini only
explains those computed values. **All figures are illustrative, not billing data,
clinical validation, or predictions of patient outcomes.**

## API and payment

Both endpoints require a completed `lap_chole` session. Unknown sessions return
404; other exercises and incomplete attempts return 409. The existing `API_KEY`
gate still applies if configured.

| GET path (under `/api/v1/sessions/{sid}`) | Response |
| --- | --- |
| `/mistake-cost/summary` | Free JSON containing only `total_usd` |
| `/mistake-cost/report` | 402 challenge, or full itemization after payment settlement |

The summary total is **modeled avoidable cost**, not the whole procedure price.
The full report includes `mistakes`, `unallocated_inefficiency_usd`, the complete
`procedure_cost` breakdown, sources, assumptions, and explanations. Existing
aggregate `/cost` and coaching endpoints retain their existing access rules.

The report implements [x402 v1](https://github.com/x402-foundation/x402/blob/main/specs/x402-specification-v1.md),
matching the requested `X-PAYMENT` header and `base-sepolia` network name. It is
not a v2 `PAYMENT-SIGNATURE` endpoint. Configure a **trusted HTTPS facilitator
that supports v1 exact Base Sepolia**, and a nonzero recipient wallet:

```dotenv
X402_PAY_TO=
X402_FACILITATOR_URL=
```

These values are intentionally empty in `.env.example`. The facilitator URL is
its API base, without `/verify` or `/settle`. No wallet private key belongs on this
server. Real settlement is **not verified**; no funded testnet wallet was used.

1. Request the report. With a configured recipient and no valid payment, the API
   returns HTTP 402 with `x402Version: 1` and `accepts` requirements.
2. A v1 wallet client signs the exact authorization and retries the same GET with
   base64-encoded payment JSON in `X-PAYMENT`.
3. The server sends the payload and its own requirements to facilitator `/verify`,
   then `/settle`. It releases the report only after successful settlement with a
   transaction hash, matching payer, and matching network.
4. The 200 response includes a base64 JSON receipt in `X-PAYMENT-RESPONSE`, exposed
   to browser clients, and `Cache-Control: no-store`.

Price: **0.01 testnet USDC**, or 10,000 atomic units. This is the report access
price, independent of the medical estimate. The asset address is
`0x036CbD53842c5426634e7929541eC2318f3dCF7e`, listed by
[Circle for Base Sepolia](https://developers.circle.com/stablecoins/usdc-contract-addresses).
Requirements include `scheme: exact`, `extra: {name: USDC, version: "2"}`,
the report URL and a 60-second timeout.

Malformed/invalid/rejected payments return 402 without the report. Missing server
configuration or facilitator failures return 503. A timeout after settlement may
mean a transaction was broadcast: reconcile with the facilitator/on-chain before
signing another payment. Settlement is never automatically retried. There is no
durable receipt recovery or reusable entitlement in this implementation: each
successful GET is a new purchase. EIP-3009 nonce replay protection is delegated to
the trusted facilitator and USDC contract; a successful verification alone never
unlocks content. Keep the externally advertised request URL correct behind a proxy.

## Deterministic costing

`scoring.lap_chole_findings()` is the single source of mistake identities and
severity. Multiple rubric findings for the same event remain separate entries,
matching existing scoring. The report does not invent a `tissue_contact` event:
laparoscopic tissue injury uses `injury`, and hand collisions use `hand_collision`.

The report calls `cost.estimate_cost()` using the recorded elapsed time, path
efficiency and major findings, then allocates its existing components:

- Lost OR time uses the existing inpatient rate ($37.45/min). Its dollar total is
  divided evenly among findings in integer cents; remaining cents go to the first
  findings. Per-mistake OR minutes are derived from that allocation. **Attribution
  to individual findings is an assumption**, not a measured delay. If there are no
  findings, this cost stays in `unallocated_inefficiency_usd`.
- Each major finding contributes `0.01 * $13,857 = $138.57` in expected adverse-event
  charges. **The adverse-event probability is an assumption** with no validated
  simulator-to-patient mapping. Minor findings receive zero adverse-event risk cost.
- A bile-duct finding adds $19,626 once per attempt, assigned to the first relevant
  finding. Applying the general-surgery complication figure to bile-duct injury is
  an assumption, not a bile-duct-specific sourced price. Subsequent related findings
  do not duplicate that complication charge.
- `total_usd` is the sum of per-mistake totals and unallocated inefficiency. Efficient
  baseline OR time and supplies are shown only in `procedure_cost`, not charged again
  per mistake. Replacement supply consumption is not measured and is not invented.

The clean counterfactual uses the already-rounded efficient OR line, so its
difference from procedure total reconciles with the itemized avoidable costs.

See [COST_DATA.md](COST_DATA.md) for the original published rates and limitations.
Historical costs and adverse-event **charges** are mixed without inflation adjustment;
the complication and risk components can overlap. The model is illustrative.

## Supply snapshot and Apify

`backend/data/supply_prices.json` is committed. Each row has `item`, numeric `usd`,
`source_url`, and timezone-qualified `retrieved_at`. URLs are citations, not claims
that the listed price is a hospital procurement price or is still available.
The initial entries were checked through web retrieval on 2026-10-24; they were
not fetched through Apify. Retrieved web pages may be cached.

| Item key and unit | Seed USD | Listing |
| --- | --- | --- |
| `hem_o_lok_cartridge`: one cartridge of six large clips | 49.00 | [Synergy Surgical, Teleflex 544240](https://www.synergysurgical.com/product/0-in-date/50-teleflex/36-ligating-clips/46207331-weck-hem-o-lok-polymer-ligating-clips-large-6-clipscartridge-544240/) |
| `trocar`: one representative disposable trocar | 107.50 | [CIA Medical, Covidien 179095P Versaport Plus V2 5–11 mm listing](https://www.ciamedical.com/5mm-trocars) |
| `suture_set`: one suture/needle-driver set | Omitted | No cited price seeded |

The modeled supply basket is **one cartridge plus five representative trocars**.
Those quantities and treating one trocar model as representative of all ports are
assumptions, not clinical purchasing recommendations. `quantity_assumed` is true.
The `assumed` flag on a supply line describes its **price**: false only for a valid
sourced snapshot row. The source URL and retrieval timestamp accompany that line.
The basket may overlap the historical laparoscopic instrument set, especially its
disposable option; it is not a deduplicated bill of materials.

If a partial snapshot lacks a component, the fallback prices are an assumed $50
per cartridge and $20 per trocar. If both are missing, the original assumed $150
combined line is retained. Suture sets retain the assumed $30 fallback unless a
sourced `suture_set` row exists. No other exercise's instrument prices change.

Set `APIFY_TOKEN` and `APIFY_PRICING_ACTOR` to run a pricing actor explicitly. The
actor must accept `{items: [...], currency: "USD"}` and return a dataset of the
four-field rows above, normalized to the exact units in the table. A generic crawler
does not automatically satisfy that contract. Configure the actor to scrape cited
listings and omit unpriced/unverifiable items; never return box prices as unit prices.

From `backend/`, after configuring `.env`:

```powershell
.\.venv\Scripts\python -c "from dotenv import load_dotenv; load_dotenv(override=True); from pricing import refresh_with_apify; print(refresh_with_apify())"
```

`refresh_with_apify()` uses Apify's
[synchronous dataset endpoint](https://docs.apify.com/api/v2/actor-run-sync-get-dataset-items-post)
with bearer authentication and bounded timeouts. It validates rows, merges supplied
items into the previous snapshot, and replaces the file atomically. Missing keys,
offline errors, malformed/empty results or failed writes retain the old snapshot.
A missing/corrupt snapshot falls back to visibly assumed prices. No refresh runs in
the request path. Review actor output/citations before deploying a refreshed snapshot;
schema validation alone cannot establish price authenticity. Live Apify refresh is
**not verified**.

## Gemini guard and verification

The report reuses `coach.py`'s Gemini URL, model, HTTP client retry helper and time
budget. The model writes one or two sentences per mistake using named money
placeholders. The server rejects literal numbers/currency amounts (including dollar
figures outside the computed set), unknown placeholders, mismatched IDs and malformed
output, then substitutes only that mistake's computed amounts. Any invalid batch,
missing API key or model failure uses deterministic explanatory templates. Gemini
does not set scores or cost fields. Live Gemini report narration is **not verified**.

Run the repository tests from `backend/`:

```powershell
.\.venv\Scripts\python -m pytest -q
```

Tests use mocked Gemini, Apify and facilitator HTTP responses. They cover arithmetic,
complication deduplication, price provenance/fallback, invented model figures, free
summaries, payment challenges, verify-and-settle flow, malformed payments, rejected
or uncertain settlement, session eligibility and API-key enforcement. These checks
do not establish live wallet interoperability or real settlement.
