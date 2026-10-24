# opencode tasks (edit only docs/ and backend/tests/; do not edit backend source, report instead)
Commit after each task.
1. Review backend/speech/ and deploy/ (written by codex). Run `cd backend && python -m pytest -q`. Check error handling, that no API key appears in logs or responses, and that a missing CARTESIA_API_KEY returns 503. Write docs/reviews/speech-codex.md: APPROVE or a short numbered fix list citing file:line.
2. Write docs/RUNBOOK.md: exact commands to start the backend, run backend/tools/fake_quest.py against it, call /coach and /coach/audio, and the env vars each needs. Run every command and say what was not verified.
