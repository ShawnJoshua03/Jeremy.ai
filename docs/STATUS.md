# Status

As of 2026-10-24. Every "verified" item names its proof. Anything not listed as verified has not been run. This docs pass read the tree and did not run pytest, Unity, the dashboard, or a live provider.

The operation is a **laparoscopic cholecystectomy** (gallbladder removal). The Unity app is **GallbladderTrainer** at `quest/Assets/GallbladderTrainer/`. The scene is `Scenes/GallbladderTraining.unity`. That push is a **starter view**: identify the cystic duct, then judge two prepared views. Dissection, clipping, and cutting are later exercises on the same session id, `lap_chole_cvs`. The bridge, guide, report, and replay stay generic. Unknown ids score as not assessed (`correct: null`). Contract: [CONTRACT_CVS.md](CONTRACT_CVS.md). Headset-free checks: [TESTING_WITHOUT_HEADSET.md](TESTING_WITHOUT_HEADSET.md).

`quest/Assets/Scripts/` is gone. The old robot avatar is only in git history at `868bafcf` (`quest/Assets/Scripts/Feedback/RobotNarrator.cs`). `quest/Builds/` is not in this tree. `quest/HANDOFF_UNITY.md` is not in this tree.

## CVS trainer (this branch)

| Item | State |
|---|---|
| GallbladderTrainer starter view, `DesktopPointer`, local feedback | Files are in `quest/Assets/GallbladderTrainer/`. The three exercises are the current `ExerciseLibrary` table. Play mode was not run. **Not verified** |
| Generic pass-through and reserved procedure events | Specified. Unknown exercise and structure ids stay in the report and replay with `correct: null`. `step_completed`, `clip_applied`, `structure_cut`, `injury`, and `critical_view` are accepted on `lap_chole_cvs` with no score until a table row exists. **Not verified** |
| Session exercise `lap_chole_cvs`, sample `instrument: "head"`, CVS event kinds | Not accepted by `backend/main.py`. The exercise literal is `peg_transfer`, `wound_closure`, `lap_chole`, `port_placement`. Samples allow `left` and `right`. **Not verified** |
| `GET /sessions/{id}/report`, `POST .../report/audio`, `GET .../replay` | No such routes in `backend/main.py`. **Not verified** |
| Golden fixtures `backend/tests/fixtures/cvs_*.json` and a schema snapshot | The fixtures directory is not in the tree. **Not verified** |
| Narrator `critical` on a false "demonstrated" for `ex_view_a` | Specified in the contract. `backend/narrator.py` still paces the fifteen-step `lap_chole` exercise. **Not verified** for `lap_chole_cvs` |
| Unity upload to the API | `TrainingModels.cs` states that no networking is implemented. `ITrainingBackendHook` is optional and unused by an HTTP client. **Not verified** |

The fifteen-step exercise id `lap_chole` is still implemented in the backend, with peg transfer, wound closure, and port placement. That is a different exercise from `lap_chole_cvs`.

## Earlier recorded backend runs

These rows are earlier 2026-10-24 write-ups. This docs pass did not re-run them.

| Item | Proof recorded earlier |
|---|---|
| Session API: create, start, samples, events, complete, metrics, cost, progress, list | `cd backend && python -m pytest -q`: 145 passed. See the test-count note |
| Deterministic scoring and sourced cost estimator | `backend/scoring.py`, `backend/cost.py`, their tests; sources in [COST_DATA.md](COST_DATA.md) |
| Idempotent uploads, idempotent session create (`client_request_id`), optional `X-API-Key`, CORS | tests in `backend/tests/` |
| Instructor annotations and command poll/ack | `backend/tests/test_commands.py`; live local run |
| Coach question, drill list, `GET /assessment`, rate limit, 12 s LLM budget | `backend/tests/test_coach_features.py`; live local run (fallback coach) |
| `POST /sessions/{id}/cancel`; UTC timestamps | tests; live local run |
| Dashboard against a live local backend | Headless Chrome with `tools/fake_quest.py` sessions |
| Claude coaching (`claude-haiku-5-5` via `backend/llm.py`) with a rule-based fallback | Unit tests only. Live Claude call **not verified**; the earlier live Claude check no longer applies |
| Cartesia `POST /coach/audio` WAV | Live call from a PC; WAV test |
| Convex dev deployment `careful-magpie-841` | Ingest 204 for the right secret, 401 for a wrong one, empty payload |
| Dashboard production build | `cd dashboard && npm run build` |
| Fifteen-step `lap_chole` scoring and `tools/fake_quest.py --exercise lap_chole` | `backend/tests/test_lap_chole.py`. A live local replay is in [API.md](API.md) |
| Narrator and mistake-cost modules | Present: `backend/narrator.py`, `backend/narrator_router.py`, `backend/mistake_cost_router.py`, `backend/tests/test_narrator.py`, `backend/tests/test_mistake_cost.py`. An earlier status said they were absent. That was before this merge. This pass did not run those tests |

## Test count

Earlier write-ups disagreed (111, 145, 136 passed). This docs pass did not re-run pytest. `backend/.venv` was not used. Narrator and mistake-cost tests exist now; they were outside the source count quoted in the previous status.

## In progress

Specified for `lap_chole_cvs` in [CONTRACT_CVS.md](CONTRACT_CVS.md). **Not verified.**

| Item | Design |
|---|---|
| Talking guide | `POST /api/v1/sessions/{sid}/narrator/line` and `POST .../narrator/line/audio`. `step` is the current exercise id from the table, including an id the table does not know. Pace target 75 s. A wrong decision on a known row is `off_track`. The starter row `ex_view_a` called "criteria demonstrated" is `critical`. An unknown id is not penalised. `request_instructor` is never penalised. Claude writes one sentence from backend numbers. HTTP 503 keeps the text. The headset holds no provider key |
| End-of-session report and replay | `GET /sessions/{sid}/report` after complete (409 before). One exercise table drives the score. Unknown ids are `correct: null` and still listed. Reserved procedure events are included with no score line until a row exists. Claude writes only `review`, `tip`, `summary`, `strengths`, and `focus_next`. `POST .../report/audio` speaks `summary`. `GET .../replay` downsamples tracks to at most 10 Hz, including `head`, and keeps every event kind |
| Mistake cost | Free `mistake-cost/summary`. Itemized `mistake-cost/report` is x402 on Base Sepolia. False "demonstrated" on `ex_view_a` is a bile-duct risk line. A wrong structure on `ex_identify` is a misidentification line. `request_instructor` is $0. Illustrative, not a bill. See [COST_DATA.md](COST_DATA.md) and [X402_SUBMISSION.md](X402_SUBMISSION.md) |

## Future tasks (ordered)

| # | Task | Blocker |
|---|---|---|
| 1 | Accept `lap_chole_cvs` per [CONTRACT_CVS.md](CONTRACT_CVS.md): one exercise table, `instrument: "head"`, unknown ids as `correct: null`, reserved procedure events on the report and replay, and those routes | None in docs. Code is another worker. New Unity code stays in its own folders |
| 2 | Add `backend/tests/fixtures/cvs_*.json` (starter session, plus unknown ids and reserved events) and the schema snapshot. Keep them passing | Task 1 |
| 3 | Post sessions from GallbladderTrainer, then run the DesktopPointer steps in [TESTING_WITHOUT_HEADSET.md](TESTING_WITHOUT_HEADSET.md) | Tasks 1–2 |
| 4 | First headset run of a GallbladderTrainer build | A Quest build. None is in `quest/Builds/` |
| 5 | Point that build at the API and read `/report` from the headset | Task 4 |
| 6 | Dashboard and Convex with a real `lap_chole_cvs` session | Task 3 |
| 7 | Deployed API on Render: dashboard https://jeremywantstobeasurgeon.us, API https://api.jeremywantstobeasurgeon.us (checked 2026-10-24 in an earlier pass). Headset against that API | Task 5 |
| 8 | Postgres | A Postgres server |
| 9 | Apify refresh into `backend/data/supply_prices.json` | No actor run recorded. **Not verified** |

## Known bugs and gaps

| Item | Where | Impact |
|---|---|---|
| CVS contract is documented and not implemented on the API or the headset upload | [CONTRACT_CVS.md](CONTRACT_CVS.md), `backend/main.py`, `GallbladderTrainer` | A Play-mode session does not produce `/report` |
| `jsonable_errors` NaN check and the 422 handler | `backend/main.py` | Readability. See [reviews/cleanup-findings.md](reviews/cleanup-findings.md). Line numbers were not re-checked |
| No login. `API_KEY` is one shared key and is off by default | backend | Anyone who can reach the API can post |
| Command and annotation routes do not check instructor identity | `backend/mentorship/` | Trust-the-network only |
| Headset feel for the deleted `Assets/Scripts/` tree (pinch, HUD, hand ray) | git history | Those scripts are gone. GallbladderTrainer on a headset is **not verified** |
| Adverse-event probability per error (0.01) is an assumption | `backend/cost.py`, [COST_DATA.md](COST_DATA.md) | Figures are illustrative |

More detail: [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md), [reviews/](reviews/).

## Not verified

This pass: pytest, Unity Play, mouse completion of the three exercises, `GET /report`, dashboard Sample preview, Claude, Cartesia, x402 settlement, Apify refresh.

Also: Quest launch, passthrough, hands, a headset-to-backend round trip, audio in the headset, Convex with a real CVS session, Postgres, and a funded Base Sepolia wallet.

Peg transfer, wound closure, port placement, and the fifteen-step `lap_chole` routes remain in the API. Their earlier proofs were not re-run here.
