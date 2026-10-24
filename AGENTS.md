# AGENTS.md

Shared context for Jeremy.ai (CalHacks 13). Read this, then `docs/HANDOFF.md`. Edit only the paths your prompt assigns.

## Stack

Unity local sim (Quest 3, passthrough) + one FastAPI backend + Gemini coaching + Cartesia speech + Convex + a React instructor dashboard. Tiger Data was dropped; Convex is a read-only live mirror. Claude and Moss are not in the stack.

The implemented scenario is **peg transfer**. `docs/ARCHITECTURE.md` also describes a wound-closure shape. Do not migrate endpoints unless that conflict is explicitly in your task.

## Paths

| Path | Owner | What it is |
|---|---|---|
| `backend/` except `backend/tests/` | claude | FastAPI session API, scoring, cost, `coach.py` |
| `backend/speech/`, `backend/mentorship/`, `deploy/` | opencode, grok | Cartesia adapter, Vultr systemd/uvicorn scripts |
| `quest/Assets/Scripts/` | codex | Unity C# |
| `dashboard/` (incl. `dashboard/convex/`) | copilot | Instructor view and Convex functions |
| `docs/`, `backend/tests/` | grok | Judging docs and tests |
| `AGENTS.md`, `.env.example` | grok | This file and env template |

## Run and test

From the repo root, in PowerShell:

```powershell
cd backend
.\.venv\Scripts\python -m pytest -q
.\.venv\Scripts\python -m uvicorn main:app --reload
```

API base path is `/api/v1`. Interactive docs are at `http://127.0.0.1:8000/docs`. Recreate the venv with `python -m venv .venv` and `.\.venv\Scripts\pip install -r requirements.txt` if `.venv` is missing.

Copy `.env.example` to `.env` for local keys. Never commit `.env`. Keys stay off the headset and out of any browser bundle.

## Rules

- Scoring is deterministic. Gemini never invents a score.
- Say an integration works only if you ran it. Otherwise write **not verified**.
- Cost figures are illustrative, not billing data. The per-error adverse-event probability is an assumption. See `docs/COST_DATA.md`.
- Run pytest with `-q` and report failures only.
