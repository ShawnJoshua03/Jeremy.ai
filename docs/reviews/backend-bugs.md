# Backend bugs exposed by tests

Bugs found by `backend/tests/` (opencode session 2). No source changes made;
source is owned by claude. Each entry lists the failing test that exposes it.

## 1. Non-finite `elapsed_s` returns 500 instead of 422

- **Failing test:** `tests/test_api_validation.py::test_non_finite_elapsed_rejected`
- **Endpoint:** `POST /api/v1/sessions/{sid}/complete` (main.py:222)
- **Repro:** send `{"elapsed_s": NaN}` as raw JSON (NaN is not expressible in
  strict JSON, but Python/Next.js clients can emit it; verified via raw body).
- **Observed:** HTTP 500 (unhandled `ValueError: Out of range float values are
  not JSON compliant: nan`). Pydantic actually rejects the value correctly
  (`less_than_equal` validation error), but FastAPI's default
  `RequestValidationError` handler then fails to JSON-encode the `input: nan`
  field of the error, so the 422 response itself raises.
- **Expected:** clean 422.
- **Impact:** low. Inconsistent-time clients crash the server response instead
  of getting a normal validation message. Same root cause applies to
  `+Inf`/`-Inf` elapsed (also asserted in the test).

## 2. Event `source`/`target` geometry is not validated

- **Failing test:** `tests/test_api_validation.py::test_event_geometry_validated_like_sample_positions`
- **Endpoint:** `POST /api/v1/sessions/{sid}/events` (main.py:201)
- **Repro:** post a `transfer_success` event with
  `source: ["NaN", 0, 0]` or `source: [1e9, 0, 0]` while sample positions are
  strictly bounded to `+/-10 m` and finite (main.py:164).
- **Observed:** HTTP 200, event stored. On completion, the garbage ideal
  distance propagates into metrics: with both a NaN-source and a 1e9-source
  event, `path_efficiency` came back as `1.0` (the NaN ideal sum is silently
  clamped to the upper bound by `_clamp`, scoring.py:95), `practice_score`
  69.3 for a 2-sample run.
- **Expected:** 422 for non-finite or out-of-range `source`/`target`, mirroring
  the sample-position check.
- **Impact:** medium for scoring integrity. In the single-event case a huge
  ideal distance clamps efficiency to 0 and deflates the score; in the NaN
  case the clamp inflates it to 1.0. Deterministic but wrong results, and
  non-finite floats sit in the SQLite/Postgres JSON columns.
