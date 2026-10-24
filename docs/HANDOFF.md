# Handoff / transition doc

Snapshot as of 2026-10-24. Read this first, then [ARCHITECTURE.md](ARCHITECTURE.md).

## What this is

**Jeremy.ai** for CalHacks 13. A trainee practices a focused surgical task on a Meta Quest 3 in passthrough, gets AI voice coaching, and can invite an instructor to watch and annotate from a browser. At the end the app prices the procedure with an itemized, source-cited cost breakdown.

Repo: https://github.com/ShawnJoshua03/Jeremy.ai (private). Collaborators with push access: SpectralGT, msde-7, christinec04 (all accepted).

## Decisions made

| Decision | Detail |
|---|---|
| Stack | Unity local sim + one FastAPI backend (Vultr) + Gemini + Cartesia + Convex + React instructor dashboard. Tiger Data optional, only for high-volume telemetry. |
| Claude | **Removed.** Gemini does all AI feedback. |
| Moss | **Removed.** Replaced by Cartesia (text-to-speech). No RAG; one scenario fits in the prompt. |
| Cost | Must use real published data. See [COST_DATA.md](COST_DATA.md). Per-error adverse-event probability is the one explicit assumption. |
| Scoring | Deterministic, rule-based. AI never invents scores. No clinical-validity claims. |
| Offline | Quest simulation must work without internet; events buffer and retry. |
| Possible extra tracks | Apify (Best Actor) and Elastic (Agent Builder) were in the sponsor screenshots. Not started; Elastic requirements unconfirmed. |

## Current state

**Done and pushed**
- `README.md`: stack and 10-item task list (all unchecked).
- `docs/ARCHITECTURE.md`: full architecture plan.
- `docs/COST_DATA.md`: sourced cost rates and the mapping from performance to dollars.
- `docs/UNITY_REFERENCES.md`: reference repos for Quest and surgical training.
- `backend/`: `scoring.py` (deterministic metrics), `database.py` (SQLAlchemy, SQLite locally, `DATABASE_URL` for Postgres/Tiger Data), `main.py` (FastAPI session API under `/api/v1`), `tests/` (6 passing).

**Not started**
- Cost module (`backend/cost.py`), Gemini coaching, Cartesia speech, Convex, React dashboard, all Unity code, deployment, `.env.example`, AGENTS.md.

## Open conflict to resolve

The existing backend is a **peg-transfer** API (`/api/v1/sessions/...`, samples + events). `ARCHITECTURE.md` describes a **wound-closure** scenario with scenario-versioned events and different endpoints (`/attempts/{id}/submit`, `/commands`, `/jobs`). Decide: keep peg transfer as scenario 1 (faster, scorer carries over) or migrate. Recommendation: keep peg transfer for the demo and add the new endpoints around it.

## Tooling on this machine

| Tool | Status |
|---|---|
| Unity | Installed: editors 6000.2.7f2, 6000.4.0f1, 6000.6.7f1 (Unity Hub). Pick one; Meta XR SDK compatibility with the chosen version is **unverified**. Check Meta's current docs before choosing. |
| Docker | **Not installed** (deploy and container tests blocked). |
| Python | 3.13; backend venv at `backend/.venv` (not committed). |
| Node | Installed. |
| gh | Installed, authenticated as ShawnJoshua03. |
| Agent CLIs | claude, codex, copilot, opencode (npm global); **grok 1.0.70** at `~/.grok/bin`, signed in and verified. Orca installed at `%LOCALAPPDATA%\Programs\orca\Orca.exe` (not on PATH). |

## Run it

```powershell
cd C:\Users\shawn\Jeremy.ai\backend
.\.venv\Scripts\python -m pytest -q                      # 6 tests
.\.venv\Scripts\python -m uvicorn main:app --reload      # API on :8000, docs at /docs
```
Recreate the venv if missing: `python -m venv .venv; .\.venv\Scripts\pip install -r requirements.txt`.

## Next steps (in order)

1. **Cost module**: `backend/cost.py` + tests from COST_DATA.md rates, wired into the complete endpoint and a `/cost` route.
2. **Unity project** in `quest/`: Meta XR All-in-One SDK, passthrough, Building Blocks, controller grab, board with 6 pegs, local state machine, event recorder, batched uploader. Start from Meta's Starter Samples and Interaction SDK samples.
3. **Backend round trip** from Quest: session, event batch, submit, metrics and cost.
4. **Gemini coaching** with Pydantic schema output and server-side validation of event IDs and exercise IDs.
5. **Cartesia speech** adapter, with text fallback.
6. **Convex + React dashboard**: live session, replay with highlighted errors, annotations and commands to the headset.
7. **Deploy to Vultr** (needs Docker or a plain systemd/uvicorn setup).
8. Docs required for judging: SPONSOR_EVIDENCE.md, DEMO_SCRIPT.md, KNOWN_LIMITATIONS.md. Do not claim sponsor eligibility or integration success that was not verified.

## Suggested agent split

| Agent | Work |
|---|---|
| claude | Backend: cost module, Gemini coordinator, API changes |
| codex | Unity C# (scene, grab, state machine, recorder, uploader) |
| copilot | React dashboard and Convex functions |
| opencode | Cartesia adapter, Vultr deployment scripts |
| grok | Tests, docs, sponsor evidence, demo script |

## Credentials needed (none committed)

`GEMINI_API_KEY`, `CARTESIA_API_KEY`, Convex deployment URL/key, Vultr account, optional Tiger Data `DATABASE_URL`. Keep all keys off the headset and out of the browser bundle. `.env` is git-ignored.

## Caveats

- Cost figures came from search summaries; verify each cited source before judging.
- Nothing has been run against Tiger Data, Gemini, Cartesia, Convex or Vultr yet.
- No Quest hardware testing has happened.
