# opencode second session tasks (edit only backend/tests/ and docs/; do not edit backend source, report instead)
Commit after each task.
1. Add API validation tests: NaN/inf and out-of-range positions, oversize batches, bad enums, unknown session ids, completing twice, events after completion. Use TestClient with a temp SQLite file DB (see existing tests).
2. Add boundary tests for backend/cost.py (zero elapsed, many errors, risk_per_error 0 and 1) and backend/scoring.py (empty samples, single sample, all invalid).
3. If a test exposes a bug, do not fix source; write it to docs/reviews/backend-bugs.md with the failing test name.
Done: cd backend && python -m pytest -q passes except documented bugs.
