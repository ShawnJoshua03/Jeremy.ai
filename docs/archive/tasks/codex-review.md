# Codex review task: review the free-model work before it merges

Branches to review (read-only; do not edit them): `tests-opencode` (ShawnJoshua03/tests-opencode: code cleanup, tests) and `speech-opencode` (docs rewrite).
For each: `git diff main...<branch>` limited to what changed, then:
1. Behavior preserved? Look for changed routes, response shapes, env var names, schema, validation, error codes, removed tests or weakened assertions. Run `cd backend && python -m pytest -q` on the branch (git worktree or checkout) and compare counts with main.
2. Docs: every claim checkable? Flag statements that say something works with no test or recorded run, broken links, deleted content.
3. Readability: leave comments only where they help; flag over-engineering, noisy comments, needless splits.
Write docs/reviews/free-model-review.md on a new branch `review-codex`: per branch APPROVE / APPROVE WITH FIXES / REJECT, then a numbered list of required fixes with file:line. Keep it short. Do not merge anything.
