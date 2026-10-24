# Handoff

Snapshot 2026-10-24. Read [STATUS.md](STATUS.md) for what is done and unverified, [README.md](README.md) for the doc index.

## What this is
**Jeremy.ai** (CalHacks 13). A trainee practices a **laparoscopic cholecystectomy** (gallbladder removal, exercise id `lap_chole`) on a Meta Quest 3. The design adds a talking robot-avatar companion (Cartesia) during the fifteen steps and, at the end, a per-mistake cost report. The detailed report sits behind an x402 testnet payment. An instructor can watch and annotate from a browser. The companion, the avatar, and the paid report are specified in [STATUS.md](STATUS.md) and are **not verified**. Peg transfer, wound closure, and port placement remain in the API.

Repo: https://github.com/ShawnJoshua03/Jeremy.ai (private).

## Decisions
| Decision | Detail |
|---|---|
| Stack | Unity 6.6 (6000.6.7f1) Quest app, one FastAPI backend, Gemini, Cartesia, Convex, React dashboard. Do not downgrade Unity |
| Source of truth | The backend database (SQLite by default; `DATABASE_URL` for Postgres). Convex is a live, read-only mirror for the dashboard |
| Dropped | Claude as a runtime dependency, Moss (no RAG needed), Tiger Data (plain SQLite/Postgres is enough) |
| Scenario | Laparoscopic cholecystectomy (`lap_chole`). Peg transfer, wound closure, and port placement stay implemented. Wound-closure route names in ARCHITECTURE.md that were never built stay future work |
| Companion | Designed: Gemini phrases a deterministic expected-vs-actual progress delta; Cartesia speaks one sentence. Unity avatar polls it. **Not verified** |
| Mistake cost | Designed: deterministic per-finding dollars, free summary, x402-gated report on Base Sepolia, Apify price refresh with an offline snapshot. **Not verified** |
| Scoring | Deterministic and rule-based. Gemini never invents a score or a dollar figure. No clinical-validity claims |
| Cost | Real published data only ([COST_DATA.md](COST_DATA.md)); the per-error adverse-event chance is one explicit assumption |
| Input | Hand tracking (pinch to grab, hand ray plus pinch for the HUD). Controllers also work in code but are untested |
| Offline | The Quest app runs without internet; uploads queue on disk and retry |

## Layout
| Path | Contents |
|---|---|
| `backend/` | FastAPI app (`main.py`), `scoring.py`, `cost.py`, `coach.py`, `database.py`, `convex_mirror.py`, `speech/`, `mentorship/`, `tools/fake_quest.py`, `tests/`. Designed and absent here: `narrator.py`, `narrator_router.py`, `pricing.py`, `x402_gate.py`, `mistake_cost_router.py`, `data/supply_prices.json` |
| `quest/` | Unity project. Scripts in `Assets/Scripts`, build tooling in `Assets/Editor`, scene `Assets/Scenes/PegTransfer.unity`. `lap_chole` is an exercise on `PegTransferRig`. The robot-avatar script is designed and is not in this tree |
| `dashboard/` | React + Vite instructor view; Convex functions in `dashboard/convex/` |
| `deploy/` | Vultr systemd setup (never run) |
| `docs/` | Documentation; see the index |

## Run it
```powershell
cd backend
python -m venv .venv; .\.venv\Scripts\pip install -r requirements.txt   # first time
.\.venv\Scripts\python -m pytest -q                                      # last recorded run: 145 passed; source count 286 cases. See STATUS.md. Not re-run in the 2026-10-24 docs pass
.\.venv\Scripts\python -m uvicorn main:app --reload                      # API on :8000, docs at /docs
.\.venv\Scripts\python -m tools.fake_quest --help                        # replay a synthetic session (no headset)
cd ..\dashboard; npm install; npm run build                              # dashboard
```
Build the Quest APK: menu `Jeremy > Build APK` in Unity, or `Unity -batchMode -quit -nographics -projectPath quest -executeMethod Jeremy.EditorTools.QuestBuild.BuildApk`. Output: `quest/Builds/Jeremy.apk`. Install: `adb install -r quest/Builds/Jeremy.apk`. More commands in [RUNBOOK.md](RUNBOOK.md).

## Credentials (never committed)
Copy `.env.example` to `.env` at the repo root: `GEMINI_API_KEY`, `GEMINI_MODEL`, `CARTESIA_API_KEY`, optional `CARTESIA_MODEL_ID` and `CARTESIA_VOICE_ID` (see [RUNBOOK.md](RUNBOOK.md)), `CONVEX_URL` (the `.convex.site` address), `CONVEX_INGEST_SHARED_SECRET` (old name `CONVEX_DEPLOY_KEY` is still accepted by the backend), optional `API_KEY`, `DATABASE_URL`. `.env` overrides stale shell variables. Keep keys off the headset and out of the browser bundle.

Designed for the companion and the paid report, and not in `.env.example` in this worktree: `X402_PAY_TO`, `X402_FACILITATOR_URL`, `APIFY_TOKEN`, `APIFY_PRICING_ACTOR`. Empty values only. No keys in the repo.

## Next steps
See the ordered list in [STATUS.md](STATUS.md). The first headset step is unchanged: enable Developer Mode (Meta Horizon phone app), connect by USB, confirm `adb devices` shows `device`, install the APK, and note where it first breaks.

The companion, the avatar, and the x402 report still have to land and be run before anyone tells a judge they work. Sponsor notes: [CARTESIA_SUBMISSION.md](CARTESIA_SUBMISSION.md), [X402_SUBMISSION.md](X402_SUBMISSION.md). Demo lines: [DEMO_SCRIPT.md](DEMO_SCRIPT.md).

## Machine notes
Unity 6000.6.7f1 with Android modules is installed. Docker Desktop is installed but needs WSL (`wsl --install`, then a restart). The Orca app and agent CLIs (codex, copilot, opencode, grok) were used for parallel work; their task files are in [archive/](archive/).
