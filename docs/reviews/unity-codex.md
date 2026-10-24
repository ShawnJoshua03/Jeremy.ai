# unity-codex review

APPROVE

Reviewed `ShawnJoshua03/unity-codex` at `31f9f6a9` (worktree `C:/Users/shawn/orca/workspaces/Jeremy.ai/unity-codex`) against current `backend/main.py`. This is a static read of the scripts. Unity, the play-mode harness, and a live upload were not re-run here.

## Scope

`git diff --stat 4bad1f62...31f9f6a9` is 52 files, +1821. Every script, asmdef, and `Tests~/Verify.ps1` is under `quest/Assets/Scripts/`. `Tests~/UnityProject~/` is gitignored and is not in the commit. The one path outside that folder is `quest/HANDOFF_UNITY.md`. That file says the user asked for it, and it contains no implementation.

## API

Uploads match the routes and bodies in `backend/main.py`:

- `POST /api/v1/sessions` sends `{trainee_id, exercise:"peg_transfer"}`.
- `POST .../start` sends `{}`. A repeated start is safe because the server keeps the first timer.
- `POST .../events` batches at most 1000. Fields are `event_id`, `kind`, `t_ms`, `instrument`, `object_id`, `source`, `target`. Kinds are `transfer_success`, `drop`, `invalid_release`. Instruments are `left` and `right`. `event_id` is a 32-character UUID. Source and target are length-3 board-local arrays.
- `POST .../samples` batches at most 1000 at 25 Hz. Fields are `instrument`, `idx` (monotonic per hand, from 0), `t_ms`, `pos` length 3, `quat` length 4, `valid`, and `interaction` (`grasping` or `idle`). Coordinates are finite and within 10 m before send. Invalid samples use pos zero and quat `(0,0,0,1)`.
- `POST .../complete` sends `{elapsed_s}` clamped to 0..3600. Events and samples drain first.
- `GET .../cost` uses the server defaults. `distance_by_instrument` is parsed as `{left, right}`, which matches the JSON object. Cost lines are a `CostLine` array.
- `POST .../coach` sends `{}` only after complete has succeeded. The panel reads `{summary, tips[].text, source}`.

Retries cover status 0, 408, 429, and 5xx. Event ids and `(instrument, idx)` make a retry safe. A lost create response can orphan a server session. The scripts document that. The server has no create idempotency key, so this is not a Unity payload bug.

`CommandPoller` calls `GET .../commands` and `POST .../commands/{id}/ack`. Those routes are not in `backend/main.py`. `pollingEnabled` defaults off, and a 404 or 405 turns polling off. The README marks the contract as proposed. It does not change session, sample, or event payloads.

## Not required

`quest/Assets/Scripts/README.md` says this branch's older checkout has no coach route. Current `main.py` has `POST /api/v1/sessions/{sid}/coach`, and `CoachPanel` already calls it. Codex's claim of 37 play-mode assertions was not re-run in this review.
