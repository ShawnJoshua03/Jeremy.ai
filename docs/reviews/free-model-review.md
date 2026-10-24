# Free model review

Reviewed on `review-codex` after fetching and merging `main`. No implementation files changed.

## `ShawnJoshua03/tests-opencode` — APPROVE WITH FIXES

No regressions found in the reviewed diff. Shared pytest bootstrap preserves the prior import path and SQLite test setup; route assertions remain intact. UTC alias and annotation changes preserve behavior. `python -m pytest -q` passes on the branch (101 tests) and on current `main` (103 tests).

1. Update the branch with current `main` and rerun pytest before merge. The branch predates the two dashboard read tests now in `backend/tests/test_dashboard_reads.py:1`, so its passing count does not cover the current suite.

## `speech-opencode` — APPROVE

`git diff main...speech-opencode` is empty, and the branch tip is an ancestor of `main`; its changes are already contained in `main`. The branch suite passes (66 tests at that revision). There are no unique documentation changes left to review.
