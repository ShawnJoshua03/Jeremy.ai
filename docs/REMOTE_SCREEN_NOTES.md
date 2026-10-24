# Remote screen: status notes

Branch `remote-screen`. Goal: a remote instructor watches the headset's rendered scene (over passthrough when available) and hears the trainee's mic plus app audio, live, in the dashboard. Transport is JPEG frames and PCM16 over WebSocket, relayed by FastAPI. Latency target is about 0.5 s. Plan: `C:\Users\Christine\.claude\plans\vivid-gathering-hippo.md` (local, not in the repo).

## What is in the branch

| Area | Files | State |
|---|---|---|
| Backend relay | `backend/live/hub.py`, `backend/live/router.py`, `backend/live/__init__.py`, one line in `backend/main.py` | Written, tested with the in-process test client |
| Backend tests | `backend/tests/test_live.py` (11 tests) | Passing |
| Dashboard panel | `dashboard/src/LivePanel.tsx`, `dashboard/src/liveStream.ts`, mount in `dashboard/src/App.tsx`, CSS appended to `dashboard/src/styles.css` | Type-checks and builds. Not run in a browser |
| Dev proxy | `dashboard/vite.config.ts` (`ws: true`) | Not tested |

## Wire protocol (as implemented)

- Binary: `[kind:u8][t_ms:u64 LE][payload]`. Kind 1 is JPEG, kind 2 is PCM16 mono.
- Publisher text: `{"type":"hello", "video":{...}, "audio":{"rate":24000,"channels":1}, "passthrough":bool}`.
- Server text: `{"type":"status","live":bool,"viewers":n,"passthrough":bool}`.
- Close codes: 4401 auth, 4403 origin, 4404 session missing, 4409 replaced by a newer publisher, 4413 frame over 512 KB, 4429 viewer limit (8).
- Routes: `WS /api/v1/sessions/{sid}/live/publish` (header `X-API-Key`), `WS .../live/watch?token=`, `POST .../live/viewer-token`, `GET .../live`.

## Test results

- `pytest -q tests/test_live.py`: **11 passed**.
- `pytest -q` (full backend): **217 passed, 10 failed**. All 10 failures are the same cause and existed before this branch's changes. `backend/main.py:536` passes `bile_duct_injury=` to `cost.estimate_cost`, and the committed `backend/cost.py` doesn't accept it. The failing tests are in `test_coach_features`, `test_dashboard_reads`, `test_fake_quest`, `test_port_placement_api`, `test_scoring_and_api`, and `test_wound_closure`. Not fixed here. It looks like part of the lap-chole WIP commit.
- `npm run build` in `dashboard/` (type check and Vite build): **passes**.
- Not verified: anything in a browser, against a live server, on a headset, or on Render.

## Environment notes

- The machine's pip could not reach pypi.org (certificate verification failed). The backend venv was installed with `pip --trusted-host pypi.org --trusted-host files.pythonhosted.org`, as approved. That turns off certificate checks for those two hosts. Fix the CA on this machine and drop the flag.
- `npm ci` worked without changes.

## Not done yet

1. **Unity side** (`quest/Assets/Scripts/Streaming/`). Not started: spectator camera and JPEG readback, passthrough camera source, audio tap on the listener, mic capture, WebSocket client with reconnect, HUD live indicator, Android permissions (`RECORD_AUDIO`, `horizonos.permission.HEADSET_CAMERA`). Cannot be compiled here. `quest/Library` is missing, so the Meta MRUK passthrough API names are unconfirmed.
2. **`backend/tools/fake_quest.py --live`**: a synthetic publisher for testing without a headset. Not written.
3. **Session list "Live" badge** in the dashboard. Not written. The panel is only shown on a selected session.
4. **Docs**: `docs/ARCHITECTURE.md` §5 still says "not a headset livestream". `docs/STATUS.md` and `docs/RUNBOOK.md` are not updated.
5. **Browser check** of the panel: unmute, reconnect, and the 4401/4404 messages.
6. **Latency and frame rate** on a real device and network. The `t_ms` value uses the headset clock, so the dashboard does not show a latency figure yet.
7. **Production auth gap.** The dashboard's viewer-token request goes out without a key, like the rest of the dashboard. If the Render API sets `API_KEY`, the panel will fail to get a token. Decide whether the dashboard should get a read-only key or the token endpoint should be public.
8. **Single instance only.** The relay is in memory, so it works on one API process. Render's free plan runs one instance, but a second instance would break viewers.
9. **The cost mismatch above** blocks 10 existing tests. It is a separate fix.

## How to try what exists

```powershell
cd backend
.\.venv\Scripts\python -m pytest -q tests/test_live.py
.\.venv\Scripts\python -m uvicorn main:app --reload
cd ..\dashboard
npm run build
```
