# Jeremy.ai

CalHacks 13. A trainee practices a focused surgical task on a Meta Quest 3, gets contextual voice coaching from an AI tutor, and can invite an instructor to observe the session and give guidance from a browser. At the end, the program measures the cost of the surgery.

## Stack

- **Meta Quest 3**: passthrough training experience
- **SQLite / Postgres**: session storage (Tiger Data was considered and dropped)
- **Google Gemini API**: interprets structured simulation events into explanations, a personalized debrief, and a structured exercise assessment
- **Cartesia**: text-to-speech for the AI coach (voice input via a separate speech-to-text service later)
- **Render**: hosting (free tier), configured by [render.yaml](render.yaml)
- **Convex**: live read-only mirror for the instructor dashboard

## Live deployment

- **Instructor dashboard:** https://jeremywantstobeasurgeon.us/ (static site on Render)
- **API:** https://api.jeremywantstobeasurgeon.us (health: `/health`, docs: `/docs`), the same service as `https://jeremy-api.onrender.com`
- Deployed from `main` on every push. Render's free tier sleeps after about 15 minutes idle, so the first request can take 50 seconds, and the SQLite data resets on restart.
- Checked on 2026-10-24: the apex returns 200, `www` redirects to the apex, and `api` `/health` returns 200. Not verified: a headset talking to this API, and Cartesia and Gemini from the Quest.
- The `deploy/` folder describes an older Vultr VM path that was never deployed.

## Quick start
```powershell
cd backend
python -m venv .venv; .\.venv\Scripts\pip install -r requirements.txt
.\.venv\Scripts\python -m pytest -q
.\.venv\Scripts\python -m uvicorn main:app --reload
```
Copy `.env.example` to `.env` for Gemini, Cartesia and Convex keys. Full instructions: [docs/RUNBOOK.md](docs/RUNBOOK.md).

## Task list

Status as of 2026-10-24. "Code done" means implemented and tested off-device; nothing has run on a Quest yet. See [docs/STATUS.md](docs/STATUS.md).

| # | Task | Status |
|---|---|---|
| 1 | Passthrough Quest experience with a board anchored in front of the user | Code done (`PegTransferRig`, `PegBoard`); APK builds, not run on a headset |
| 2 | Grab a virtual instrument with a controller (or hand pinch) | Code done (`InstrumentController`); not run on device |
| 3 | Complete a peg transfer | Code done; editor harness passes |
| 4 | Quantitative results: path efficiency, time, mistakes | Done (`backend/scoring.py`, HUD, dashboard). Smoothness is not computed |
| 5 | Replay the movement in a browser with errors highlighted | Done (dashboard replay, checked in a browser against a live backend) |
| 6 | Ask why performance suffered and get a targeted practice recommendation | Done (`POST /coach` with `question`, `next_exercise`). Gemini verified live from a PC; rule-based fallback when unavailable |
| 7 | Separate structured exercise assessment | Done (`GET /assessment`, deterministic) |
| 8 | Speak coaching aloud through Cartesia, with a text fallback | Code done (`/coach/audio`, `CoachAudioPlayer`); Cartesia WAV verified live from a PC; headset playback not verified |
| 9 | Remote mentor annotation appears in the headset | Code done: dashboard notes and highlight/pause/retry are queued and polled by the headset. Headset side not run on device |
| 10 | Cost of the surgery with an itemized breakdown | Done (`GET /cost`, HUD) |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full architecture plan.
- Cost model data and citations: [docs/COST_DATA.md](docs/COST_DATA.md)
- Unity / Quest reference repos: [docs/UNITY_REFERENCES.md](docs/UNITY_REFERENCES.md)
- Handoff: [docs/HANDOFF.md](docs/HANDOFF.md)
- Status and open work: [docs/STATUS.md](docs/STATUS.md)
- All docs: [docs/README.md](docs/README.md)
