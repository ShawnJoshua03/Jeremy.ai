# Status

As of 2026-10-24. Every "verified" item names its proof. Anything not listed as verified has not been run.

The scenario is a **laparoscopic cholecystectomy** (gallbladder removal), exercise id `lap_chole`, fifteen steps in [LAP_CHOLE.md](LAP_CHOLE.md). A talking robot-avatar companion and an x402-paid per-mistake cost report are specified under In progress. Their modules are not in this worktree, and neither has been run. Peg transfer, wound closure, and port placement stay in the API.

## Completed
| Item | Proof |
|---|---|
| Session API: create, start, samples, events, complete, metrics, cost, progress, list, replay reads | Last recorded `cd backend && python -m pytest -q`: 145 passed (2026-10-24). See the test-count note under this table |
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
| Laparoscopic cholecystectomy scoring, cost lines, coach drills, and `tools/fake_quest.py --exercise lap_chole` | `backend/tests/test_lap_chole.py`. A live local replay is recorded in [API.md](API.md) (2026-10-24, fallback coach). This docs pass did not re-run it |

## Test count

Earlier write-ups disagreed: HANDOFF said 111 tests, this file said 145 passed, and [SPONSOR_EVIDENCE.md](SPONSOR_EVIDENCE.md) says 136 passed. Those are separate 2026-10-24 runs, and 111 is stale. This docs pass did not re-run pytest. `backend/.venv` is not in this worktree. A source count of `backend/**/test_*.py` the same day is 220 test functions, 286 cases after expanding `@pytest.mark.parametrize`. Narrator and mistake-cost tests are not in that count.

## In progress

Specified for this round. The files are not in this worktree. **Not verified.**

| Item | Design |
|---|---|
| Talking companion | `backend/narrator.py` and `backend/narrator_router.py`. `POST /api/v1/sessions/{sid}/narrator/line` returns `{text, step, expected_progress, actual_progress, delta, severity, repeat}`. `POST .../narrator/line/audio` returns `audio/wav`. A pure function compares expected progress on the current lap-chole step with actual progress and sets `severity` to `on_track`, `slow`, `off_track`, or `critical`. Gemini turns those facts into one sentence of at most 25 words. It does not invent the numbers. A template is used when Gemini is unavailable. The same line is not spoken twice in a row. Cartesia is the existing `backend/speech/cartesia.py` adapter. A missing key or a provider failure is 503 and the client keeps the text. Write-up: [CARTESIA_SUBMISSION.md](CARTESIA_SUBMISSION.md) |
| Unity robot avatar | A primitive robot head near the operating field polls `/narrator/line` about every 6–10 seconds during `lap_chole`. When `repeat` is false it fetches `/narrator/line/audio`, plays the WAV, and drives the mouth from the amplitude. Eye tint follows severity (on track green, slow amber, off track or critical red). A 503 keeps the sentence on screen as a subtitle. The headset does not call Cartesia or Gemini. No avatar script is in `quest/Assets/Scripts/` in this worktree |
| Per-mistake cost report | `GET /api/v1/sessions/{sid}/mistake-cost/summary` is free and returns the total. `GET .../mistake-cost/report` returns HTTP 402 with x402 payment requirements until a valid `X-PAYMENT` header is verified, then the itemized report and `X-PAYMENT-RESPONSE`. Network is Base Sepolia testnet, scheme `exact`, asset USDC, a small price (the design uses about $0.01), `payTo` from `X402_PAY_TO`, facilitator `X402_FACILITATOR_URL`. Each lap-chole finding is priced with `backend/cost.py`. Gemini writes one or two sentences per mistake and may not introduce a dollar figure. Apify (`APIFY_TOKEN`, `APIFY_PRICING_ACTOR`) refreshes `backend/data/supply_prices.json` and the snapshot is the offline fallback. Write-up: [X402_SUBMISSION.md](X402_SUBMISSION.md). The clip-and-trocar line in `backend/cost.py` is still the assumed $150 |

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
| 11 | Apify refresh of supply prices into `backend/data/supply_prices.json`, then `cost.py` prefers a cited snapshot over assumed constants | Specified with the mistake-cost report. No actor run. **Not verified** |

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
Quest hardware (launch, passthrough, hands, grab, HUD), the live headset-to-backend round trip, audio playback in the headset, Convex and the dashboard with real session data, Postgres, controller input.

Also not verified, because the code is not in this worktree and has not been run: the narrator endpoints, Cartesia audio for narrator lines, the Unity robot avatar, Gemini's one-sentence narration, the mistake-cost summary and 402 report, x402 verification and settlement on Base Sepolia, the Apify price refresh, and Gemini's per-mistake explanation. Facilitator checks are specified to be mocked in tests. A funded testnet wallet has not been used.
