# Handoff

Snapshot 2026-10-24. Read [STATUS.md](STATUS.md) for what is done and unverified, [README.md](README.md) for the doc index.

## What this is
**Jeremy.ai** (CalHacks 13). A trainee practices a peg-transfer task on a Meta Quest 3 in passthrough using hand tracking, gets AI coaching (Gemini text, Cartesia voice), and an instructor can watch and annotate from a browser. At the end the app prices the procedure with an itemized, source-cited cost estimate.

Repo: https://github.com/ShawnJoshua03/Jeremy.ai (private).

## Decisions
| Decision | Detail |
|---|---|
| Stack | Unity 6.6 (6000.6.7f1) Quest app, one FastAPI backend, Gemini, Cartesia, Convex, React dashboard. Do not downgrade Unity |
| Source of truth | The backend database (SQLite by default; `DATABASE_URL` for Postgres). Convex is a live, read-only mirror for the dashboard |
| Dropped | Claude as a runtime dependency, Moss (no RAG needed), Tiger Data (plain SQLite/Postgres is enough) |
| Scenario | Peg transfer. The wound-closure routes in ARCHITECTURE.md are future work |
| Scoring | Deterministic and rule-based. AI never invents scores. No clinical-validity claims |
| Cost | Real published data only ([COST_DATA.md](COST_DATA.md)); the per-error adverse-event chance is one explicit assumption |
| Input | Hand tracking (pinch to grab, hand ray plus pinch for the HUD). Controllers also work in code but are untested |
| Offline | The Quest app runs without internet; uploads queue on disk and retry |

## Layout
| Path | Contents |
|---|---|
| `backend/` | FastAPI app (`main.py`), `scoring.py`, `cost.py`, `coach.py`, `database.py`, `convex_mirror.py`, `speech/`, `mentorship/`, `tools/fake_quest.py`, `tests/` |
| `quest/` | Unity project. Scripts in `Assets/Scripts`, build tooling in `Assets/Editor`, scene `Assets/Scenes/PegTransfer.unity` |
| `dashboard/` | React + Vite instructor view; Convex functions in `dashboard/convex/` |
| `deploy/` | Vultr systemd setup (never run) |
| `docs/` | Documentation; see the index |

## Run it
```powershell
cd backend
python -m venv .venv; .\.venv\Scripts\pip install -r requirements.txt   # first time
.\.venv\Scripts\python -m pytest -q                                      # 111 tests
.\.venv\Scripts\python -m uvicorn main:app --reload                      # API on :8000, docs at /docs
.\.venv\Scripts\python -m tools.fake_quest --help                        # replay a synthetic session (no headset)
cd ..\dashboard; npm install; npm run build                              # dashboard
```
Build the Quest APK: menu `Jeremy > Build APK` in Unity, or `Unity -batchMode -quit -nographics -projectPath quest -executeMethod Jeremy.EditorTools.QuestBuild.BuildApk`. Output: `quest/Builds/Jeremy.apk`. Install: `adb install -r quest/Builds/Jeremy.apk`. More commands in [RUNBOOK.md](RUNBOOK.md).

## Credentials (never committed)
Copy `.env.example` to `.env` at the repo root: `GEMINI_API_KEY`, `GEMINI_MODEL`, `CARTESIA_API_KEY`, `CONVEX_URL` (the `.convex.site` address), `CONVEX_DEPLOY_KEY` (shared ingest secret, also set in Convex as `CONVEX_INGEST_SHARED_SECRET`), optional `API_KEY`, `DATABASE_URL`. `.env` overrides stale shell variables. Keep keys off the headset and out of the browser bundle.

## Next steps
See the ordered list in [STATUS.md](STATUS.md). The first one is the first headset run: enable Developer Mode (Meta Horizon phone app), connect by USB, confirm `adb devices` shows `device`, install the APK, and note where it first breaks.

## Machine notes
Unity 6000.6.7f1 with Android modules is installed. Docker Desktop is installed but needs WSL (`wsl --install`, then a restart). The Orca app and agent CLIs (codex, copilot, opencode, grok) were used for parallel work; their task files are in [archive/](archive/).
