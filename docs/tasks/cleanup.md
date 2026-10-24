# Cleanup task: make the repo handoff-ready (NO behavior changes)

You are acting as a professional software engineer preparing this repo for handoff to a new team.
Goal: clean, readable, deployable code with no loose ends. Behavior must stay identical.

## Hard rules
- Do not change behavior, public routes, response shapes, env var names, DB schema or test expectations.
- `cd backend && python -m pytest -q` must pass before and after every commit (run it first and record the count).
- One small commit per file group; message says what was tidied. Never reformat whole files without need.
- If something looks like a bug, do NOT fix it: add it to docs/reviews/cleanup-findings.md with file:line.
- Do not touch: quest/ (Unity, separate pass), dashboard/ (copilot owns), backend/main.py until grok's branch merges (phase 2).

## Phase 1 scope: backend/ (except main.py), backend/tools, backend/tests, deploy/
The docs/ folder belongs to the docs agent (docs/tasks/docs-cleanup.md); do not edit it except docs/reviews/cleanup-findings.md.
1. Remove dead code, unused imports/variables, commented-out code, stray debug prints, leftover TODOs (turn real ones into entries in docs/reviews/cleanup-findings.md).
2. Functions over ~40 lines or nested more than 3 deep: split only where the split is obvious and safe. Names must say what things do.
3. Every module gets a 1-3 line docstring saying its job. Comments explain why, never what. No noisy comments.
4. Type hints on public functions. Consistent style (run `ruff check` and `ruff format --check` if available; apply only safe fixes).
5. Remove duplicated helpers (e.g. repeated finite/range checks, repeated session lookups) by reusing one.
6. Repo hygiene: check .gitignore covers .venv, __pycache__, *.db, .env, node_modules, Unity Library/Temp/Builds; no secrets, no generated or scratch files committed (git ls-files review).
7. requirements.txt: pinned or bounded, nothing unused, nothing missing (verify by installing into a fresh venv and running pytest).

## Phase 2 (after Claude says main.py is merged): backend/main.py
Split into routers by concern only if it stays behavior-identical (sessions, metrics/cost, coach); keep imports and startup identical.

## Done when
pytest count unchanged or higher, `git diff --stat` reviewed by you for accidental behavior changes, findings file written, short summary of what changed at the top of docs/reviews/cleanup-findings.md.
