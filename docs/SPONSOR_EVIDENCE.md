# Sponsor evidence

Checked 2026-10-24 in this worktree. Sponsor-track eligibility was **not confirmed**. Nothing below is a claim that a prize track was entered or won.

"Verified" means a command in this session exercised that behavior. A unit test that mocks an HTTP API does **not** verify the live vendor.

## Verified by running

`cd backend` then `python -m pytest -q`: **136 passed** on 2026-10-24 (second pass; first pass was 27). The interpreter was a fresh `backend/.venv` built from `requirements.txt`.

| Piece | What the run showed |
|---|---|
| FastAPI session API | Create, start, samples, events, idempotent complete, metrics, trainee progress. Duplicate sample and event IDs are ignored. Out-of-range coordinates are rejected. |
| Deterministic scoring | Path length skips invalid samples and gaps over 250 ms. An object scores once. Practice score stays in 0–100. |
| Cost endpoint | `GET /api/v1/sessions/{id}/cost` returns four itemized lines from the rates in `backend/cost.py` after the session is completed, and 409 before that. |
| Coach endpoint, offline path | `POST /api/v1/sessions/{id}/coach` returns 404 for an unknown session and 409 before completion. With no `GEMINI_API_KEY`, the response `source` is `fallback` and transfer-success events are not sent as error tips. |
| Coach validation, mocked HTTP | Tests stub the Gemini HTTP call. Unknown event IDs are cleared. A non-200 response, invalid JSON, a schema miss, and a connection error all return the fallback. The live Gemini API was **not** called. |
| React instructor dashboard (local only) | Ran against a local backend in headless Chrome on 2026-10-24: session list with scores, replay, notes, highlight command, coach question and assessment. Not deployed. |
| Coach question, next drill, assessment | `POST /coach` with a question and `GET /assessment` against a live local uvicorn, fallback coach (no Gemini key). |
| Instructor note to headset queue | A dashboard note became an `add_annotation` command on `GET /commands` with `API_KEY` enforced. The headset applying it was **not** run. |

## Not verified

| Planned piece | Status |
|---|---|
| Gemini (`GEMINI_API_KEY`, model `gemini-3.5-flash`) | **Verified live from a PC on 2026-10-24**: `POST /coach` returned `source: gemini` after a retry on a 503. Not verified on a headset. |
| Cartesia speech | **Verified live from a PC on 2026-10-24**: `POST /coach/audio` returned a 44.1 kHz mono WAV. Headset playback is not wired or verified. |
| Convex | Functions deployed to a dev deployment; `/v1/mirror` returned 204 with the right secret and 401 with a wrong one, using an empty payload. Not verified with real session data. |
| Meta Quest 3 / Unity | **Not verified.** No project build and no headset run. Unity 6.6 (6000.6.7f1) is the decided editor version. |
| Vultr | **Not verified.** Nothing was deployed. Docker is not installed on this machine. |
| Tiger Data | **Not verified.** Tests use in-memory SQLite. `DATABASE_URL` for Postgres is unused here. |
| Apify (Best Actor) | **Not started. Not verified.** |
| Elastic (Agent Builder) | **Not started. Not verified.** Requirements were not confirmed. |

Do not tell a judge that any row in the second table is integrated.
