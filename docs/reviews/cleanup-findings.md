# Cleanup findings (Phase 1)

Prepared for handoff. Summary of what changed, then findings that were **not**
fixed because fixing them would change behavior, leave the owning path, or
rewrite whole files.

## What changed (Phase 1: backend/ except main.py, backend/tools, backend/tests, deploy/)

- `ruff check --fix` safe fixes across the allowed paths: import sorting,
  `datetime.UTC` alias, one import alias cleanup. 101 tests passed before and
  after (103 after merging main's latest).
- `backend/tests/conftest.py` and `backend/speech/tests/conftest.py`: the
  sys.path/env bootstrap now lives in one place instead of 13 test modules.
- `backend/mentorship/models.py`: dropped the duplicated `_now()` helper;
  timestamps default to `database._now` like every other row.
- Type hints added on public functions missing them: `database.make_engine`,
  `database.init_db`, `convex_mirror._iso/_epoch_ms`.
- `backend/requirements.txt`: bounded versions; verified by installing into a
  fresh venv and running pytest (101 passed, later 103 with the merged main).
- `deploy/backend.env.example`: removed the stale "used when the Gemini
  coaching changes have been merged" comment.
- `.gitignore` already covers `.venv`, `__pycache__`, `*.db`, `.env`,
  `node_modules`, `.pytest_cache` and the Unity folders; nothing added.
  `git ls-files` reviewed: no secrets, no scratch or generated files tracked;
  only empty key placeholders in `.env.example` / `deploy/backend.env.example`.
- No dead code, commented-out code, or debug prints found in the phase-1 paths
  (the only `print` is `tools/fake_quest.py`'s intended CLI output).
- No module over ~40 lines needed an obvious split in the phase-1 paths;
  `cartesia.text_to_audio`, `coach.generate`, and `convex_mirror.post_snapshot`
  are at or under the limit once their docstrings are excluded.
- Every module in the phase-1 paths already carries a job-statement docstring;
  no new ones were needed.
- Earlier out-of-scope doc edits (stale test counts, model names) were reverted
  and moved here: docs/ now belongs to the docs agent (`docs/tasks/docs-cleanup.md`).

## Findings (not fixed)

### backend/main.py (out of phase-1 scope; claude owns, phase 2)

1. `backend/main.py:42` — `jsonable_errors` NaN-hide trick `v == v` compares a
   value with itself to detect NaN. ruff flags PLR0124. It works (NaN != NaN)
   but is unreadable; suggest a `math.isnan` helper in phase 2.
2. `backend/main.py:37` — the validation-error handler strips `ctx` entirely,
   which also drops harmless data (e.g. `le: 3600.0`) from the 422 detail.
   Behavior, but worth reconsidering in phase 2 before any client relies on it.

### Intentional, keep as-is (recorded so nobody redraws them)

3. `backend/convex_mirror.py:134,141,155,162` — bare `except Exception` is the
   documented "mirroring never fails the caller" contract. ruff BLE001.
4. `backend/main.py` (11×) and mentors' routes — `Depends(...)` in argument
   defaults is idiomatic FastAPI; ruff B008 is noise here. Phase 2.
5. `backend/speech/cartesia.py:47` — two nested `with` blocks could be one
   statement (ruff SIM117); left alone to avoid churning the streaming block.
6. `ruff format` is NOT clean repo-wide. Running it would reformat whole files,
   which the task forbids doing without need. `ruff check` is clean except the
   intentional cases above (all outside phase-1 files).

### Stale docs facts for the docs agent (do not belong to this agent)

7. `docs/HANDOFF.md:31,105` — says 27 tests; the suite now has 103.
8. `docs/HANDOFF.md` "Not started: Cartesia speech, Convex, React dashboard,
   all Unity code, deployment" — all of those have code in the repo now; only
   the live integrations remain unverified (see KNOWN_LIMITATIONS.md).
9. `docs/SPONSOR_EVIDENCE.md:23` and `docs/KNOWN_LIMITATIONS.md:12` — name the
   Gemini default as `gemini-2.5-flash`; `backend/coach.py:16` and
   `.env.example` use `gemini-3.5-flash`.
10. `backend/mentorship/router.py:3` and `docs/API.md:191` reference
    `docs/HANDOFF_MENTORSHIP.md`, which is not in the repo. Someone should
    either commit it or drop the reference.
