# Review: speech adapter and deployment (codex)

Scope: `backend/speech/`, `deploy/`, and the router registration in `backend/main.py:25`.
Reviewed 2026-10-24 on Windows with PowerShell 5.1, Python 3.13.5, FastAPI TestClient,
and a live local uvicorn on `127.0.0.1:8000`.

## Verdict

**APPROVE.** No required fixes.

## Checks

1. **Tests.** `cd backend && .\.venv\Scripts\python -m pytest -q` -> `66 passed`
   (3 deprecation warnings: `on_event`, Starlette TestClient/httpx). Speech tests cover
   the provider contract, configuration overrides, missing keys, invalid text, upstream
   statuses, network errors, invalid WAV, the size cap, 404/422/503, and text fallback.
2. **Missing key returns 503.** `cartesia.py:33-35` raises `SpeechUnavailable` before any
   HTTP call; `router.py:37-43` maps it to `503` with the generic detail
   `Speech is unavailable; use text feedback.` and `Cache-Control: no-store`. Verified
   live: an empty `CARTESIA_API_KEY` returned exactly that 503.
3. **No key leakage.** The adapter does not log; it sends the key only in the
   `Authorization` header (`cartesia.py:49`). Provider errors are caught and re-raised
   `from None` with a fixed message (`cartesia.py:59-61`), so upstream bodies, headers,
   transcripts, and keys cannot reach the response or traceback. Verified live with a fake
   key: Cartesia returned 401, the client received the generic 503, and the uvicorn log
   contained only the method/path/status (no key, no provider body). A request with an
   extra `api_key` field is rejected with 422 by `extra="forbid"` (`router.py:12`).
4. **Error handling and provider limits.** Redirects are not followed, so the bearer key
   cannot be forwarded (`cartesia.py:47`); connect timeout is 5 s and read timeout 30 s;
   responses are streamed and capped at 10 MiB (`cartesia.py:54-58`); a non-WAV body is
   rejected (`cartesia.py:63-64`); invalid/blank/over-2,000-character text is rejected
   before any call (`cartesia.py:31-32`, `router.py:13-20`). The synchronous route runs in
   FastAPI's worker thread and closes the DB session before the paid call
   (`router.py:32-38`).
5. **Contract.** The endpoint, `Cartesia-Version: 2026-08-14`, bearer auth, `sonic-3.6`,
   default voice `db6b0ed5-d5d3-463d-ae85-518a07d3c2b4`, and WAV/pcm_s16le/44.1 kHz payload
   match the current Cartesia bytes API docs (checked 2026-10-24).
6. **404/422 behavior.** Live checks: unknown session -> 404 before any provider call;
   blank text, extra field, and 2,001 characters -> 422.
7. **Deployment.** `deploy/setup.sh` passes `bash -n`; it requires root and Ubuntu
   (`setup.sh:6-18`), refuses an overlapping or symlinked `/opt/jeremy` (`setup.sh:25-33`),
   keeps `/etc/jeremy/backend.env` root-owned mode 0600 (`setup.sh:41-48`), excludes
   `.env`, databases, caches, and the venv from rsync (`setup.sh:55-58`), and health-checks
   after restart (`setup.sh:66-74`). The unit runs unprivileged with a read-only code tree,
   writable state only in `/var/lib/jeremy`, no capabilities, and one worker
   (`deploy/jeremy-api.service:8-32`). Missing Cartesia credentials do not block startup.

## Non-blocking notes

- The API has no authentication or paid-request rate limit; `backend/speech/README.md:38-41`
  and `deploy/README.md:39-41` both say to use a trusted gateway. That is an accepted
  baseline limitation, not a speech adapter defect.
- Live successful synthesis (HTTP 200 WAV) and Quest playback were **not verified**: no
  real Cartesia key was available. The deployed Ubuntu/systemd/Caddy path was also
  **not executed** (no VM here); only shell syntax and the local uvicorn path were checked.
- `main.py` loads `.env` with `override=True`; a local `.env` therefore beats shell
  variables. Keep that in mind when rotating keys on the VM (the deploy rsync excludes
  `.env`, so systemd's environment file is authoritative there).
