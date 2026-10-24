# Known limitations

As of 2026-10-24. If a later session runs something in the "not verified" list, update [SPONSOR_EVIDENCE.md](SPONSOR_EVIDENCE.md) rather than deleting the limitation here until that run exists.

## Product

- The shipped backend is a **peg-transfer** API under `/api/v1/sessions`, plus SQLite instructor commands at `GET/POST /api/v1/sessions/{id}/commands` and `POST .../commands/{commandId}/ack`. `docs/ARCHITECTURE.md` still describes `/attempts/{id}/submit` and `/jobs`. Those two routes are not implemented. Command routes do not read Convex and do not check an instructor identity.
- Quest hardware, an Android/IL2CPP build, and passthrough have not been run. On 2026-10-24 the isolated editor harness (`quest/Assets/Scripts/Tests~/Verify.ps1`, Unity 6000.6.7f1) exited 0 with `JEREMY VERIFICATION PASSED: 30 assertions`. That run did not set a backend URL, so the live upload round trip inside the harness did not run. Offline buffering on a headset is still not verified.
- The practice score is a fixed weighted rubric in `backend/scoring.py`. It is not a clinical assessment and it is not a probability of operative success.
- No login, session invite, or rate limit is enforced. The API trusts whoever can reach it.
- Coaching does not take a free-text question. `POST /coach` builds tips from stored metrics and non-success events.
- The fallback coach is rule text. It does not call Gemini. Live Gemini, the model name `gemini-2.5-flash`, Cartesia, Convex, the dashboard, Vultr, and Tiger Data are **not verified**.

## Cost model

- Every total is an illustrative estimate, not a bill and not a hospital quote.
- OR minutes default to California FY2014 inpatient **$37.45**. That figure excludes surgeon professional fees. The cross-study mean **$46.04** is inflation-adjusted to 2022 dollars inside a review whose own authors describe inconsistent methods and a large SD ($32.31).
- Instrument prices are 1994 dollars (Apelgren et al.). The code does not inflate them.
- The adverse-event input is a difference of **median charges** ($13,857), not a cost. Multiplying by `0.01` per simulated error is an assumption. No cited paper links a peg-transfer drop to an intraoperative adverse event.
- Inefficiency cost re-prices minutes already implied by a low path-efficiency ratio. It is a model, not a measured extra incision-to-close time.
- Source wordings that the opened papers do **not** support are listed in [COST_DATA.md](COST_DATA.md) under Source check: the old "~$47–51" reusable range, a "$385–515" disposable range, treating the $16–$388 reusable savings as the result for all 4,724 procedures, and a Medicare payment delta of +$18,022 (the abstract's means differ by $18,023).

## Engineering

- Tests use in-memory SQLite. Postgres and Tiger Data were not started.
- Docker is not installed here, so no container run and no container deploy.
- Completing a session stores metrics once. A second complete returns the stored metrics and ignores a new `elapsed_s` (covered by test).
- Samples farther than 10 m from the origin are rejected. Tracking gaps longer than 250 ms are not counted as motion.
- `GEMINI_MODEL` defaults in code and in `.env.example`. That default was not confirmed with a successful API response.
