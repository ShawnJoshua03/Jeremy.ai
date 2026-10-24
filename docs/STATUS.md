# Status

As of 2026-10-24. Every "verified" item names its proof. Anything not listed as verified has not been run.

## Completed
| Item | Proof |
|---|---|
| Session API: create, start, samples, events, complete, metrics, cost, progress, list, replay reads | `cd backend && python -m pytest -q`: 111 passed |
| Deterministic scoring and sourced cost estimator | `backend/scoring.py`, `backend/cost.py`, their tests; sources in COST_DATA.md |
| Idempotent uploads (events, samples), idempotent session create (`client_request_id`), optional `X-API-Key`, CORS | tests in `backend/tests/` |
| Instructor annotations and command poll/ack | `backend/tests/test_commands.py` |
| Gemini coaching (`gemini-3.5-flash`), validated output, retry on 503, rule-based fallback | Live call from a PC on 2026-10-24 returned `source: gemini`; unit tests |
| Cartesia speech: `POST /coach/audio` returns a 44.1 kHz mono WAV, sizes finalized for Unity | Live call from a PC on 2026-10-24; WAV test |
| Convex: functions deployed to dev deployment `careful-magpie-841`; backend mirror | Ingest returned 204 for the right secret and 401 for a wrong one, with an empty payload |
| Instructor dashboard (React + Vite) | `cd dashboard && npm run build` passes |
| Unity scripts for peg transfer (state machine, board, instruments, detector, recorder, sampler, uploader, HUD, coach, command poller) | Compile with zero errors; headless harness in `quest/Assets/Scripts/Tests~/` (see quest/HANDOFF_UNITY.md) |
| Hand tracking: pinch grasp on `OVRHand`, hand-ray HUD pointer, tracked hands in scene | Compiles; APK builds. Not run on a headset |
| Quest APK | `quest/Builds/Jeremy.apk` (about 60 MB); `aapt2` manifest shows hand tracking, passthrough, Vulkan, arm64. Rebuild with menu `Jeremy > Build APK` or `Unity -batchMode -executeMethod Jeremy.EditorTools.QuestBuild.BuildApk` |

## In progress
None assigned. All agent branches are merged.

## Future tasks (ordered)
| # | Task | Blocker |
|---|---|---|
| 1 | First headset run: install APK, check launch, hands, grab, HUD ray, results | Developer Mode and `adb devices` on the headset |
| 2 | Tune pinch threshold, HUD position and hand-ray alignment from what the headset shows | Task 1 |
| 3 | Point the APK at the backend (set `backendOrigin` to `http://<pc-ip>:8000`, run uvicorn with `--host 0.0.0.0`, open port 8000), then verify the live round trip | Task 1 |
| 4 | Play `/coach/audio` in the headset (`CoachAudioPlayer`) | Task 3 |
| 5 | Show real sessions on the dashboard and in Convex end to end | Task 3 |
| 6 | Have the Unity client send `client_request_id` on session create | Small client change |
| 7 | Controller input check when controllers arrive (code path exists, untested) | Hardware |
| 8 | Deploy to Vultr (systemd path in `deploy/`) | Vultr account |
| 9 | Run against Postgres | A Postgres server (Docker needs WSL; WSL is not installed) |
| 10 | Phase 2 cleanup: split `backend/main.py` into routers | None; low priority |
| 11 | Optional: Apify Actor that fetches the cited cost sources and emits a dated dataset for `cost.py` | Track rules not confirmed |

## Known bugs and gaps not fixed
| Item | Where | Impact |
|---|---|---|
| Duplicate `libopenxr_loader.so` (Unity OpenXR and Meta OVRPlugin) resolved by keeping the first copy | `quest/Assets/Editor/GradlePackagingFix.cs` | If the wrong copy wins, the app may start without tracking. Check first on a headset |
| `jsonable_errors` uses `v == v` to detect NaN; the 422 handler strips `ctx` | `backend/main.py:37-42` | Readability only; see reviews/cleanup-findings.md |
| No login, invite or rate limit; `X-API-Key` is a single shared key and is off by default | backend | Anyone who can reach the API can post commands |
| Lost session-create response orphans a session until the client sends `client_request_id` | Unity `ApiClient` | Minor |
| Command and annotation routes do not check instructor identity | `backend/mentorship/` | Trust-the-network only |
| HUD placement, pinch feel, hand-ray accuracy never seen | Unity scene | Likely first tuning items |
| Cost model: adverse-event probability per error (0.01) is an assumption; several sources were checked only via search summaries | `backend/cost.py`, COST_DATA.md | Figures are illustrative |

More detail: [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md), [reviews/](reviews/).

## Not verified
Quest hardware (launch, passthrough, hands, grab, HUD), the live headset-to-backend round trip, audio playback in the headset, Convex and the dashboard with real session data, Vultr deploy, Postgres, controller input, the Apify idea.
