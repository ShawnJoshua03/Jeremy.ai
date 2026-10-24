# Status

As of 2026-10-24. Every "verified" item names its proof. Anything not listed as verified has not been run.

## Completed
| Item | Proof |
|---|---|
| Session API: create, start, samples, events, complete, metrics, cost, progress, list, replay reads | `cd backend && python -m pytest -q`: 145 passed |
| Deterministic scoring and sourced cost estimator | `backend/scoring.py`, `backend/cost.py`, their tests; sources in COST_DATA.md |
| Idempotent uploads (events, samples), idempotent session create (`client_request_id`), optional `X-API-Key`, CORS | tests in `backend/tests/` |
| Instructor annotations and command poll/ack; notes are also queued to the headset; command history with applied/rejected/expired state; expired commands skipped | `backend/tests/test_commands.py`; live local run 2026-10-24 |
| Coach takes an optional trainee question and recommends a drill from a fixed list; deterministic `GET /assessment`; per-session rate limit and a 12 s Gemini budget | `backend/tests/test_coach_features.py`; live local run (fallback coach) |
| `POST /sessions/{id}/cancel` for abandoned attempts (Quest sends it); session timestamps returned as UTC | tests; live local run |
| Dashboard against a live local backend: session list with scores, replay, notes, highlight/pause/retry, coach and assessment panel, attempt comparison | Headless Chrome with `tools/fake_quest.py` sessions, 2026-10-24 |
| Desktop preview: press Play in the Unity editor with no headset; mouse/keyboard drive both instruments | Compiled in Unity 6000.6.7f1 with Meta XR SDK 207 on 2026-10-24; play-through not yet recorded |
| Gemini coaching (`gemini-3.5-flash`), validated output, retry on 503, rule-based fallback | Live call from a PC on 2026-10-24 returned `source: gemini`; unit tests |
| Cartesia speech: `POST /coach/audio` returns a 44.1 kHz mono WAV, sizes finalized for Unity | Live call from a PC on 2026-10-24; WAV test |
| Convex: functions deployed to dev deployment `careful-magpie-841`; backend mirror | Ingest returned 204 for the right secret and 401 for a wrong one, with an empty payload |
| Instructor dashboard (React + Vite) | `cd dashboard && npm run build` passes |
| Unity scripts for peg transfer (state machine, board, instruments, detector, recorder, sampler, uploader, HUD, coach, command poller) | Compile with zero errors (re-checked 2026-10-24 in Unity 6000.6.7f1 with Meta XR SDK 207 resolved); headless harness in `quest/Assets/Scripts/Tests~/` (see quest/HANDOFF_UNITY.md) |
| Hand tracking: pinch grasp on `OVRHand`, hand-ray HUD pointer, tracked hands in scene | Compiles; APK builds. Not run on a headset |
| Quest APK | `quest/Builds/Jeremy.apk` (about 60 MB); `aapt2` manifest shows hand tracking, passthrough, Vulkan, arm64. Rebuild with menu `Jeremy > Build APK` or `Unity -batchMode -executeMethod Jeremy.EditorTools.QuestBuild.BuildApk` |

## In progress
None assigned. All agent branches are merged.

## Future tasks (ordered)
| # | Task | Blocker |
|---|---|---|
| 1 | First headset run: install APK, check launch, hands, grab, HUD ray, results | Developer Mode and `adb devices` on the headset |
| 2 | Tune pinch threshold, HUD position and hand-ray alignment from what the headset shows | Task 1 |
| 3 | Point the APK at the backend (on `PegTransferRig` set `backendOrigin` to `http://<pc-ip>:8000` and tick **Allow Lan Http**, run uvicorn with `--host 0.0.0.0`, open port 8000), then verify the live round trip | Task 1 |
| 4 | Play `/coach/audio` in the headset (`CoachAudioPlayer`) | Task 3 |
| 5 | Show real sessions on the dashboard and in Convex end to end | Task 3 |
| 7 | Controller input check when controllers arrive (code path exists, untested) | Hardware |
| 8 | Deployed on Render, not Vultr: dashboard https://jeremywantstobeasurgeon.us, API https://api.jeremywantstobeasurgeon.us (checked 2026-10-24). See `render.yaml`. Remaining: headset to this API | Done, headset check open |
| 9 | Run against Postgres | A Postgres server (Docker needs WSL; WSL is not installed) |
| 10 | Phase 2 cleanup: split `backend/main.py` into routers | None; low priority |
| 11 | Optional: Apify Actor that fetches the cited cost sources and emits a dated dataset for `cost.py` | Track rules not confirmed |

## Known bugs and gaps not fixed
| Item | Where | Impact |
|---|---|---|
| Duplicate `libopenxr_loader.so` (Unity OpenXR and Meta OVRPlugin) resolved by keeping the first copy | `quest/Assets/Editor/GradlePackagingFix.cs` | If the wrong copy wins, the app may start without tracking. Check first on a headset |
| `jsonable_errors` uses `v == v` to detect NaN; the 422 handler strips `ctx` | `backend/main.py:37-42` | Readability only; see reviews/cleanup-findings.md |
| No login or invite; `API_KEY` (X-API-Key or Bearer) is a single shared key and is off by default; only coach and audio are rate-limited | backend | Anyone who can reach the API can post commands |
| Command and annotation routes do not check instructor identity | `backend/mentorship/` | Trust-the-network only |
| HUD placement, pinch feel, hand-ray accuracy never seen on a headset. The pinch point now uses `XRHand_*` bone IDs on OpenXR hand skeletons (the legacy IDs are ring-finger bones there) | Unity scene, `InstrumentController.cs` | Likely first tuning items |
| Cost model: adverse-event probability per error (0.01) is an assumption; several sources were checked only via search summaries | `backend/cost.py`, COST_DATA.md | Figures are illustrative |

More detail: [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md), [reviews/](reviews/).

## Not verified
Quest hardware (launch, passthrough, hands, grab, HUD), the live headset-to-backend round trip, audio playback in the headset, Convex and the dashboard with real session data, Postgres, controller input, the Apify idea.
