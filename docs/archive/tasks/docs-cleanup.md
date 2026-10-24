# Docs task: make docs/ the single, honest picture of the project

Audience: a new engineer or judge who has never seen this repo. Main job: be informative.
Rule zero: every statement must be checkable. Verify against `git log`, the code, and `cd backend && python -m pytest -q`. Never claim something works unless a test or a recorded run shows it; mark everything else "not verified".

## Deliverables (edit only docs/ and the root README.md; do not touch code)
1. `docs/README.md`: an index. One line per doc saying what it is and who it is for, grouped: Start here, Architecture and API, Operations (runbook, deploy, database), Product and judging (demo, sponsors, limitations), Reviews, Archive.
2. `docs/STATUS.md`: one page, updated from facts, with these sections:
   - **Completed**: shipped features, each with proof (test file, commit, or recorded run).
   - **In progress**: owner (agent) and branch.
   - **Future tasks**: ordered, with the blocker (key, hardware, account, decision).
   - **Known bugs and gaps not yet fixed**: pulled from docs/reviews/*.md, docs/KNOWN_LIMITATIONS.md, README files in backend/speech and quest/Assets/Scripts. Each: what, where (file:line), impact, workaround.
   - **Not verified**: Quest hardware, real Cartesia audio, deployment on Vultr, Postgres, Tiger Data (dropped), etc.
3. Rewrite `docs/HANDOFF.md` to be short (under ~120 lines): what this is, decisions made, how to run, current state pointing to STATUS.md, next steps. Remove stale text (Unity "not verified" lines that are now done, "Open conflict" if resolved, old agent-split details that STATUS.md supersedes).
4. Resolve contradictions between HANDOFF, ARCHITECTURE, API, README (peg transfer is the demo scenario; Convex is a live mirror; Tiger Data dropped; Claude is not a runtime dependency; Gemini does AI feedback). ARCHITECTURE.md: mark sections that describe unbuilt wound-closure endpoints as "future" rather than deleting.
5. Move per-agent operational files (docs/tasks/, docs/AGENT_PROMPTS.md) into `docs/archive/` once their work is complete; keep their content, fix links. Do not delete any doc.
6. Root `README.md`: short pitch, quick start (3 commands), link to docs/README.md, honest task checklist that matches STATUS.md.
7. Check every relative link in docs/ resolves; list any you cannot fix.

## Style
Plain, short sentences. Tables for status. No marketing language, no emojis, no duplicated paragraphs; link instead of copying. Dates in ISO format.

## Done when
Index and STATUS exist, links resolve, no doc contradicts another, summary of changes appended to docs/STATUS.md under "Doc changelog". Commit small and often.
