# Handoff

Snapshot 2026-10-24. Read [STATUS.md](STATUS.md) for what is verified, then [CONTRACT_CVS.md](CONTRACT_CVS.md). The doc index is [README.md](README.md).

## What this is

**Jeremy.ai** (CalHacks 13). A trainee practices a **laparoscopic cholecystectomy** (gallbladder removal) on a Meta Quest 3.

The Unity app on this branch is **GallbladderTrainer** (`quest/Assets/GallbladderTrainer/`). The scene is `Scenes/GallbladderTraining.unity`. The trainee identifies the cystic duct (`ex_identify`), then judges two prepared views (`ex_view_a` expects "not demonstrated", `ex_view_b` expects "demonstrated"). The frozen session exercise id is `lap_chole_cvs`.

`quest/Assets/Scripts/` is deleted. The old robot avatar is in git history at commit `868bafcf` (`quest/Assets/Scripts/Feedback/RobotNarrator.cs`), for reference only. There is no APK under `quest/Builds/`.

The backend still implements the fifteen-step exercise `lap_chole`, plus peg transfer, wound closure, and port placement. It does not yet accept `lap_chole_cvs`. GallbladderTrainer does not upload. Speech, the holographic report, replay, and the x402 itemized cost for this trainer are specified and **not verified**.

Repo: https://github.com/ShawnJoshua03/Jeremy.ai (private). This worktree branch is `cvs-docs`.

## Decisions

| Decision | Detail |
|---|---|
| Stack | Unity 6.6 (6000.6.7f1) Quest app, one FastAPI backend, Gemini, Cartesia, Convex, React dashboard. Do not downgrade Unity |
| Source of truth | The backend database (SQLite by default; `DATABASE_URL` for Postgres). Convex is a live, read-only mirror for the dashboard |
| Dropped | Claude as a runtime dependency, Moss, Tiger Data |
| Scenario | Laparoscopic cholecystectomy. The CVS trainer contract uses exercise id `lap_chole_cvs`. The fifteen-step `lap_chole` exercise remains in the API. Peg transfer, wound closure, and port placement stay implemented |
| Contract | [CONTRACT_CVS.md](CONTRACT_CVS.md) is frozen v1. Add fields, do not rename ids, bump the version for a break, and keep `backend/tests/fixtures/cvs_*.json` passing once those files exist |
| Guide | Gemini phrases a deterministic pace and severity. Cartesia speaks one sentence. For `lap_chole_cvs`, a false "demonstrated" on view A is `critical`. **Not verified** |
| Report | `GET /sessions/{id}/report` after complete. Numbers are deterministic. Gemini writes only the prose fields. **Not verified** (route not in `backend/main.py`) |
| Mistake cost | Deterministic dollars, free summary, x402-gated itemized report on Base Sepolia. Illustrative, not a bill. **Not verified** for the CVS lines |
| Scoring | Deterministic. Gemini never invents a score or a dollar figure |
| Desktop | `DesktopPointer` is the mouse path in the editor. Play mode was not run. **Not verified** |
| Offline | The old Scripts uploader queued to disk. That tree is gone. GallbladderTrainer has no networking |

## Layout

| Path | Contents |
|---|---|
| `backend/` | FastAPI (`main.py`), scoring, cost, coach, narrator, mistake-cost, speech, mentorship, `tools/fake_quest.py`, tests. No `tests/fixtures/cvs_*.json` |
| `quest/Assets/GallbladderTrainer/` | The Unity app. Runtime, editor scene builder, scene `Scenes/GallbladderTraining.unity` |
| `dashboard/` | React + Vite instructor view. Convex functions in `dashboard/convex/`. Sample preview is synthetic peg-transfer data |
| `deploy/` | Vultr systemd notes. The live host recorded earlier is Render (`render.yaml`). Vultr was not used |
| `docs/` | [CONTRACT_CVS.md](CONTRACT_CVS.md), [TESTING_WITHOUT_HEADSET.md](TESTING_WITHOUT_HEADSET.md), [DEMO_SCRIPT.md](DEMO_SCRIPT.md) |

## Run it

```powershell
cd backend
python -m venv .venv; .\.venv\Scripts\pip install -r requirements.txt   # first time
.\.venv\Scripts\python -m pytest -q                                      # not run in this docs pass
.\.venv\Scripts\python -m uvicorn main:app --reload                      # API on :8000, docs at /docs
```

Desktop scene check (not run here): open `quest`, open `Assets/GallbladderTrainer/Scenes/GallbladderTraining.unity`, press Play, use the mouse. The steps and the `/report` check are in [TESTING_WITHOUT_HEADSET.md](TESTING_WITHOUT_HEADSET.md). That check cannot pass until the trainer posts a session.

Dashboard:

```powershell
cd dashboard
npm install
npm run dev
```

Sample preview is in the sidebar. It does not load a CVS report.

There is no GallbladderTrainer APK in this tree. Do not install `quest/Builds/Jeremy.apk`. That path is absent.

## Credentials (never committed)

Copy `.env.example` to `.env` at the repo root. Keys in that example: `GEMINI_API_KEY`, `GEMINI_MODEL`, `CARTESIA_API_KEY`, `DATABASE_URL`, `CORS_ORIGINS`, `API_KEY`, `CONVEX_URL`, `CONVEX_INGEST_SHARED_SECRET`, `COACH_RATE_LIMIT_PER_MIN`, `APIFY_TOKEN`, `APIFY_PRICING_ACTOR`, `X402_PAY_TO`, `X402_FACILITATOR_URL`. Empty values only. No keys in the repo. Keep keys off the headset and out of the browser bundle.

The dashboard example is `dashboard/.env.example` (`VITE_API_URL`, `VITE_PROXY_TARGET`, `VITE_CONVEX_URL`, `DASHBOARD_API_KEY`).

## Next steps

The ordered list is in [STATUS.md](STATUS.md). First code gap: implement `lap_chole_cvs` from [CONTRACT_CVS.md](CONTRACT_CVS.md) and add the golden fixtures. First manual gap: press Play on `GallbladderTraining.unity` and write down what the mouse can click. Do not tell a judge the guide spoke, the report rendered, or a payment settled until that run is on screen.

Demo lines: [DEMO_SCRIPT.md](DEMO_SCRIPT.md). Sponsor notes: [CARTESIA_SUBMISSION.md](CARTESIA_SUBMISSION.md), [X402_SUBMISSION.md](X402_SUBMISSION.md).

## Machine notes

Unity 6000.6.7f1 was recorded as installed in an earlier pass. This pass did not launch it. Docker Desktop needs WSL (`wsl --install`, then a restart) for local Postgres. The Orca app and agent CLIs were used for parallel work. Old task files are in [archive/](archive/).
