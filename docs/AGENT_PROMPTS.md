# Agent prompts (paste into Orca, one worktree per agent)

Shared rule for all: read only `docs/HANDOFF.md` and the files you own. Edit only your paths. Run tests with `-q`. Commit small, then stop. Do not claim an integration works unless you ran it.

## codex: Unity C#
Blocked until the human creates the Unity 6.6 project in `quest/` (HANDOFF steps 1-6).
```
Task: implement the Unity scripts in the HANDOFF.md "Scene and scripts to build" table, under quest/Assets/Scripts/ only.
Order: ExerciseStateMachine, PegBoard, InstrumentController, TransferDetector, EventRecorder, PoseSampler, ApiClient+UploadQueue.
API contract: backend/main.py (/api/v1/sessions, /start, /events, /samples, /complete, /cost). Positions in board-local frame.
Confirm Meta SDK/OpenXR API names from the installed package, not memory.
Done: scripts compile in Unity with no errors; list anything untested on device.
```

## copilot: dashboard and Convex
```
Task: create dashboard/ (React + Vite + TS) and convex/ for the instructor view.
Features: live session list and metrics, replay timeline with error events highlighted, annotation and command (highlight/pause/retry) sending.
Data source: backend REST at /api/v1 (read VITE_API_URL); Convex only for live annotations/commands.
No secrets in the bundle. Done: `npm run build` passes; note any feature that is mocked.
```

## opencode: Cartesia and deploy
```
Task 1: backend/speech/cartesia.py, a function text->audio bytes using CARTESIA_API_KEY (from env), plus POST /api/v1/sessions/{id}/coach/audio in a new router file (do not edit main.py beyond one include_router line). Missing key or API failure returns 503 so clients show text only. Verify request format against Cartesia docs.
Task 2: deploy/ with a systemd unit and setup script for uvicorn on a Vultr Ubuntu VM (Docker is not available locally).
Tests: mock the HTTP call. Done: pytest -q passes; state what was not run against the real service.
```

## grok: docs and tests
```
Task: write docs/SPONSOR_EVIDENCE.md, docs/DEMO_SCRIPT.md, docs/KNOWN_LIMITATIONS.md, and AGENTS.md (short shared context: stack, paths, run/test commands).
Rules: only state integrations that were verified by running them; mark everything else "not verified". Check each source cited in docs/COST_DATA.md and list any that do not support the figure.
Also add edge-case tests in backend/tests/ for scoring and the coach endpoint. Done: pytest -q passes.
```

## claude (already done / next)
Backend round trip and `/coach` are done. Next: verify Gemini model name and request format against Google's docs once a key exists.
