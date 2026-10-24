# API

Local FastAPI app. HANDOFF starts it on port 8000. The bodies below are the raw JSON from `curl.exe` against `http://127.0.0.1:8765` on 2026-10-24. `GEMINI_API_KEY` was empty, so coach returned the rule-based fallback. Live Gemini was **not** called. Ids and timestamps change on every run.

SQLite round-trips drop the trailing `Z` on some datetimes. That is what these responses contained.

Replace `{id}` with the `id` from create. The captured id was `5f5ea34168e148f9bce017745c9ec128`.

## GET /health

```powershell
curl.exe -s http://127.0.0.1:8765/health
```

`200`

```json
{"status":"ok"}
```

## CORS

`CORS_ORIGINS` is a comma-separated origin list. When it is unset or blank, the only origin is `http://localhost:5173`. Every method is allowed. Requested headers are reflected, including `Content-Type` and `X-API-Key`. Credentials are not allowed. An origin outside the list still gets the normal status code and no `Access-Control-Allow-Origin` header.

## API key

`API_KEY` is optional. When it is unset or blank, `/api/v1` behaves as before. When it is set, every `/api/v1` route requires the key, either as `X-API-Key: <key>` or as `Authorization: Bearer <key>`. The Quest client sends `X-API-Key` from `PegTransferRig.backendApiKey`. The dashboard never holds the key: the Vite dev proxy adds it from `DASHBOARD_API_KEY`, and in production Caddy adds it (see `deploy/Caddyfile.example`). A missing or wrong key is `401` `{"detail":"invalid api key"}`. `GET /health` stays open. `OPTIONS` preflight does not require the key, so the browser can send `X-API-Key` on the real request. A `401` from an allowed origin still includes `Access-Control-Allow-Origin`.

Checked with FastAPI `TestClient` on 2026-10-24. Not captured with curl. The value used in that test was not a deployed secret. `tools/fake_quest.py` sends the header on every call when `API_KEY` is set and does not print the key.

Checked with FastAPI `TestClient` on 2026-10-24. `OPTIONS /api/v1/sessions` with `Origin: http://localhost:5173`, `Access-Control-Request-Method: GET`, and `Access-Control-Request-Headers: content-type,x-api-key`:

`200`

```
access-control-allow-origin: http://localhost:5173
access-control-allow-headers: content-type,x-api-key
access-control-allow-methods: DELETE, GET, HEAD, OPTIONS, PATCH, POST, PUT, QUERY
access-control-max-age: 600
```

## POST /api/v1/sessions

```powershell
curl.exe -s -X POST http://127.0.0.1:8765/api/v1/sessions -H "Content-Type: application/json" --data-binary "@session.json"
```

`session.json` was `{"trainee_id":"judge","exercise":"peg_transfer"}`.

`201`

```json
{"id":"5f5ea34168e148f9bce017745c9ec128","trainee_id":"judge","exercise":"peg_transfer","status":"ready","created_at":"2026-10-24T08:10:22.783397Z","started_at":null,"completed_at":null,"elapsed_s":null}
```

Optional `client_request_id`. It is trimmed. A blank value is treated as omitted. A non-blank value must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$` or the response is `422`. The first create is still `201`. The same id with the same `trainee_id` and `exercise` is `200` and returns the stored session, including a later `running` or `completed` status. A different `trainee_id` or `exercise` is `409` `{"detail":"client_request_id already exists"}`. The response does not include `client_request_id`. Omitting the field still creates a new session every time.

Checked with FastAPI `TestClient` on 2026-10-24 (in-memory SQLite). Not captured with curl. `200` for a repeated `req-1` after start:

```json
{"id":"5e97b8d164ce493bb3b3f487deefe217","trainee_id":"ada","exercise":"peg_transfer","status":"running","created_at":"2026-10-24T09:59:16.959675","started_at":"2026-10-24T09:59:16.969925","completed_at":null,"elapsed_s":null}
```

`tools/fake_quest.py` sends `client_request_id` (`--client-request-id`, or a new id when the flag is omitted). A second replay of an already completed session reads the stored metrics and reports this script's trace as duplicates (`150` samples, `8` events) without posting another upload. A completed session still rejects a real sample upload.

## GET /api/v1/sessions

Dashboard session picker. Query parameters: `trainee_id`, `status` (`ready`, `running`, `completed`, or `cancelled`), and `limit` (default 50, minimum 1, maximum 100). A blank `trainee_id` or `status` is ignored. An unknown status is `422`. `limit` outside 1–100 is `422`. A trainee with no sessions returns `[]`. Rows are newest `created_at` first, then `id`.

Checked with FastAPI `TestClient` on 2026-10-24 (in-memory SQLite). Not captured with curl. SQLite dropped the trailing `Z`.

`200` for `?trainee_id=ada&status=running&limit=1` after create and start:

```json
[{"id":"22f849dcaeee4874b3b87249b23e2bd0","trainee_id":"ada","exercise":"peg_transfer","status":"running","created_at":"2026-10-24T09:40:37.794265","started_at":"2026-10-24T09:40:37.820000","completed_at":null,"elapsed_s":null,"metrics":null}]
```

Every session object (this list, `GET /sessions/{id}`, create and start) also has `metrics`: `null` until the session is completed, then the same object `GET /metrics` returns. The dashboard uses it to show scores in the session list without one request per session.

Unknown status `paused`:

`422`

```json
{"detail":"status must be ready, running, completed, or cancelled"}
```

## POST /api/v1/sessions/{id}/start

```powershell
curl.exe -s -X POST http://127.0.0.1:8765/api/v1/sessions/5f5ea34168e148f9bce017745c9ec128/start
```

`200`

```json
{"id":"5f5ea34168e148f9bce017745c9ec128","trainee_id":"judge","exercise":"peg_transfer","status":"running","created_at":"2026-10-24T08:10:22.783397","started_at":"2026-10-24T08:10:23.064643Z","completed_at":null,"elapsed_s":null}
```

## POST /api/v1/sessions/{id}/samples

Accepted while status is `ready` or `running`. This call was the first upload.

```powershell
curl.exe -s -X POST http://127.0.0.1:8765/api/v1/sessions/5f5ea34168e148f9bce017745c9ec128/samples -H "Content-Type: application/json" --data-binary "@samples.json"
```

`samples.json`:

```json
{"samples":[{"instrument":"left","idx":0,"t_ms":0,"pos":[0.0,0.0,0.0],"valid":true,"interaction":"idle"},{"instrument":"left","idx":1,"t_ms":40,"pos":[0.0,0.04,0.0],"valid":true,"interaction":"holding"}]}
```

`200`

```json
{"accepted":2,"duplicates":0}
```

## GET /api/v1/sessions/{id}/samples

```powershell
curl.exe -s http://127.0.0.1:8765/api/v1/sessions/5f5ea34168e148f9bce017745c9ec128/samples
```

`200`

```json
[{"instrument":"left","idx":0,"t_ms":0,"pos":[0.0,0.0,0.0],"valid":true,"interaction":"idle"},{"instrument":"left","idx":1,"t_ms":40,"pos":[0.0,0.04,0.0],"valid":true,"interaction":"holding"}]
```

## POST /api/v1/sessions/{id}/events

Rejected once the session is `completed`. This call was the first upload.

```powershell
curl.exe -s -X POST http://127.0.0.1:8765/api/v1/sessions/5f5ea34168e148f9bce017745c9ec128/events -H "Content-Type: application/json" --data-binary "@events.json"
```

`events.json`:

```json
{"events":[{"event_id":"drop-1","kind":"drop","t_ms":20,"instrument":"left","object_id":"peg-1"},{"event_id":"xfer-1","kind":"transfer_success","t_ms":40,"instrument":"left","object_id":"peg-1","source":[0.0,0.0,0.0],"target":[0.0,0.12,0.0]}]}
```

`200`

```json
{"accepted":2,"duplicates":0}
```

## GET /api/v1/sessions/{id}/events

Replay. Ordered by `t_ms`, then insert order. A session with no events returns `[]`. An unknown session is `404` `{"detail":"session not found"}`.

Checked with FastAPI `TestClient` on 2026-10-24 (in-memory SQLite). Not captured with curl.

`200`

```json
[{"event_id":"early","kind":"transfer_success","t_ms":40,"instrument":"left","object_id":"peg-1","source":null,"target":null},{"event_id":"late","kind":"drop","t_ms":80,"instrument":"right","object_id":"peg-2","source":[0.1,0.0,0.0],"target":[0.2,0.0,0.0]}]
```

## GET /api/v1/sessions/{id}/annotations

Replay. Rows are stored with `at_ms`. The list is ordered by that value, and each object also has `t_ms` set to the same number. A session with no annotations returns `[]`. An unknown session is `404` `{"detail":"session not found"}`.

Checked with FastAPI `TestClient` on 2026-10-24 (in-memory SQLite). Not captured with curl.

Create is `POST /api/v1/sessions/{id}/annotations` with `{"at_ms":90,"text":"watch the drop","author":"coach"}` (`201`). The note is also queued for the headset as an `add_annotation` command with id `ann_<annotation_id>`, message equal to the text, and a 10-minute expiry; the `201` body includes that `command_id`. Send `"send_to_headset": false` to store the note only (`command_id` is then `null`). The read after that post:

`200`

```json
[{"annotation_id":"b3bb5a3bb6cc448e9b03d7b52e134206","t_ms":90,"at_ms":90,"text":"watch the drop","author":"coach"}]
```

## GET /api/v1/sessions/{id}/metrics

While `running`, this is a live preview and `elapsed_s` is `0.0`. After complete, it is the stored metrics.

```powershell
curl.exe -s http://127.0.0.1:8765/api/v1/sessions/5f5ea34168e148f9bce017745c9ec128/metrics
```

`200` while running, before complete:

```json
{"completion_pct":16.7,"successful_transfers":1,"drops":1,"invalid_releases":0,"elapsed_s":0.0,"distance_by_instrument":{"left":0.04},"total_distance_m":0.04,"path_efficiency":1.0,"practice_score":60.8}
```

`200` after complete:

```json
{"completion_pct":16.7,"successful_transfers":1,"drops":1,"invalid_releases":0,"elapsed_s":5.96,"distance_by_instrument":{"left":0.04},"total_distance_m":0.04,"path_efficiency":1.0,"practice_score":60.4}
```

## POST /api/v1/sessions/{id}/complete

Only from `running`. This call completed the session that the samples and events above belong to.

```powershell
curl.exe -s -X POST http://127.0.0.1:8765/api/v1/sessions/5f5ea34168e148f9bce017745c9ec128/complete -H "Content-Type: application/json" --data-binary "@complete.json"
```

`complete.json` was `{"elapsed_s":5.96}`.

`200`

```json
{"completion_pct":16.7,"successful_transfers":1,"drops":1,"invalid_releases":0,"elapsed_s":5.96,"distance_by_instrument":{"left":0.04},"total_distance_m":0.04,"path_efficiency":1.0,"practice_score":60.4}
```

## POST /api/v1/sessions/{id}/cancel

Closes an attempt the trainee abandoned (reset, an instructor retry, or an attempt interrupted by an app restart). From `ready` or `running` it sets `status` to `cancelled` and returns the session (`200`). Repeating it returns the same session. A completed session is `409` `{"detail":"cannot cancel a completed session"}`. After cancel, samples, events and complete are `409`. `GET /sessions?status=cancelled` lists them. The Quest upload queue sends this once per cancelled attempt that already has a server session (best effort, 3 tries).

## GET /api/v1/sessions/{id}

```powershell
curl.exe -s http://127.0.0.1:8765/api/v1/sessions/5f5ea34168e148f9bce017745c9ec128
```

`200`

```json
{"id":"5f5ea34168e148f9bce017745c9ec128","trainee_id":"judge","exercise":"peg_transfer","status":"completed","created_at":"2026-10-24T08:10:22.783397","started_at":"2026-10-24T08:10:23.064643","completed_at":"2026-10-24T08:10:23.469879","elapsed_s":5.96}
```

## GET /api/v1/sessions/{id}/cost

Only after complete. Defaults: `setting=inpatient`, `instruments=reusable`, assumed risk `0.01`. Dollars are illustrative.

```powershell
curl.exe -s "http://127.0.0.1:8765/api/v1/sessions/5f5ea34168e148f9bce017745c9ec128/cost"
```

`200`

```json
{"total_usd":191.09,"lines":[{"label":"OR time (efficient portion)","quantity":0.1,"unit_usd":37.45,"total_usd":3.72,"source":"Childers & Maggard-Gibbons, JAMA Surg 2018;153(4):e176233","assumed":false},{"label":"OR time lost to inefficient movement","quantity":0.0,"unit_usd":37.45,"total_usd":0.0,"source":"Childers & Maggard-Gibbons, JAMA Surg 2018;153(4):e176233","assumed":false},{"label":"Instruments (reusable)","quantity":1,"unit_usd":48.8,"total_usd":48.8,"source":"Apelgren et al., Surg Endosc 1994 (PMID 8153862)","assumed":false},{"label":"Expected cost of errors","quantity":1,"unit_usd":138.57,"total_usd":138.57,"source":"Financial impact of intraoperative adverse events in abdominal surgery (PMID 26026793)","assumed":true}],"counterfactual_clean_usd":52.52,"avoidable_usd":138.57,"disclaimer":"Illustrative estimate, not billing data."}
```

## POST /api/v1/sessions/{id}/coach

Only after complete (`409` `{"detail":"session not completed"}` before, `404` for an unknown id). A live Gemini response was **not verified**.

Body is optional. No body, `{}`, and `{"question":"   "}` all give the generic debrief (the Quest sends `{}`). `{"question":"..."}` is stripped and must be at most 300 characters after stripping, else `422`. A non-string question is `422`.

Response fields:

- `summary`, `tips[{text,event_id}]`: as before. An `event_id` Gemini returns that is not a real error event of this session is set to `null`.
- `answer`: reply to the question, or `null` when no question was asked. Gemini gets the question as delimited, untrusted data and is told to answer only about this attempt. The fallback cannot read free text: it says so and points at the most relevant assessment criterion (keyword match, else the weakest criterion).
- `next_exercise_id`, `next_exercise{id,title,description,exercise_id}`: recommended drill from the fixed allowlist `coach.DRILLS` (`peg_transfer`, `peg_transfer_slow_accuracy`, `peg_transfer_economy`, `peg_transfer_nondominant`). Every drill's `exercise_id` is `peg_transfer`, the only exercise Unity implements; the variant is a coaching focus. A missing or unknown id from Gemini is replaced by the rule-based choice: errors `needs_work` → slow accuracy, else path efficiency `needs_work` → economy of motion, else bimanual balance `needs_work` → non-dominant lead, else repeat. Levels come from `GET /assessment` below.
- `source`: `gemini` or `fallback`, as before. Gemini 429/500/503 are retried as before; any failure returns the fallback. A Gemini `answer` longer than 400 characters is trimmed, not discarded.

Rate limit: `POST /coach` and `POST /coach/audio` each allow `COACH_RATE_LIMIT_PER_MIN` requests per session per minute (default 12, `0` disables). Over the limit is `429` `{"detail":"too many coaching requests; try again shortly"}` with `Retry-After` in seconds. The counter is in-process, which fits the single-worker deployment.

Captured with FastAPI `TestClient` on 2026-10-24, no `GEMINI_API_KEY`, one transfer, one drop, 95 s. `POST /coach` with no body:

`200`

```json
{"summary":"1 of 6 transfers in 95s with 1 errors. Practice score 32/100.","tips":[{"text":"A drop at 1.5s. Slow down and confirm the grasp before moving.","event_id":"drop-1"},{"text":"Your instrument paths were indirect. Move straight from source to target.","event_id":null}],"answer":null,"next_exercise_id":"peg_transfer_economy","next_exercise":{"id":"peg_transfer_economy","title":"Peg transfer: economy of motion","description":"Plan each move and travel straight from peg to peg with no wandering.","exercise_id":"peg_transfer"},"source":"fallback"}
```

Same session, `{"question":"Why did I drop the peg?"}`: same body except

```json
"answer":"The AI coach is offline, so I can't answer that directly. The most relevant measurement is errors (drops + invalid releases): 1 count, rated developing. Suggested next drill: Peg transfer: economy of motion."
```

`{"question":"xxx…"}` with 301 characters: `422` `{"detail":[{"type":"string_too_long","loc":"('body', 'question')","msg":"String should have at most 300 characters"}]}`.

## GET /api/v1/sessions/{id}/assessment

Deterministic, metric-only rating of a completed attempt. No Gemini call. `404` `{"detail":"session not found"}` for an unknown id, `409` `{"detail":"session not completed"}` before complete.

Each criterion has `id`, `label`, `value`, `unit`, `level` and `rule`. `level` is `needs_work`, `developing`, `proficient`, or `not_assessed` when there is no data (no tracked motion for path efficiency or bimanual balance) or, for time, when not all 6 transfers were completed. `practice_score` is the stored score from `/metrics`; nothing is rescored. Bimanual balance is shorter ÷ longer instrument travel distance. Smoothness is not reported because the scorer does not compute it.

Thresholds live only in `backend/assessment.py` `THRESHOLDS`. They are **prototype thresholds, not clinically validated** and not FLS standards.

| Criterion | Metric | Proficient | Developing |
|---|---|---|---|
| `completion` | `completion_pct` | ≥ 100 | ≥ 50 |
| `time` | `elapsed_s` | ≤ 120 | ≤ 200 |
| `errors` | `drops + invalid_releases` | ≤ 0 | ≤ 2 |
| `path_efficiency` | `path_efficiency` | ≥ 0.7 | ≥ 0.5 |
| `bimanual_balance` | `distance_by_instrument` | ≥ 0.6 | ≥ 0.3 |

Same `TestClient` session as the coach example:

`200`

```json
{"practice_score":32.0,"criteria":[{"id":"completion","label":"Completion","value":16.7,"unit":"%","level":"needs_work","rule":"proficient if >= 100, developing if >= 50, else needs_work"},{"id":"time","label":"Time","value":95.0,"unit":"s","level":"not_assessed","rule":"proficient if <= 120, developing if <= 200, else needs_work; rated only when all 6 transfers are complete"},{"id":"errors","label":"Errors (drops + invalid releases)","value":1.0,"unit":"count","level":"developing","rule":"proficient if <= 0, developing if <= 2, else needs_work"},{"id":"path_efficiency","label":"Path efficiency","value":0.1,"unit":"ratio","level":"needs_work","rule":"proficient if >= 0.7, developing if >= 0.5, else needs_work"},{"id":"bimanual_balance","label":"Bimanual balance","value":0.0,"unit":"ratio","level":"needs_work","rule":"proficient if >= 0.6, developing if >= 0.3, else needs_work"}],"thresholds_note":"Prototype thresholds, not clinically validated."}
```

## GET /api/v1/trainees/{trainee_id}/progress

Completed sessions only.

```powershell
curl.exe -s http://127.0.0.1:8765/api/v1/trainees/judge/progress
```

`200`

```json
[{"session_id":"5f5ea34168e148f9bce017745c9ec128","completed_at":"2026-10-24T08:10:23.469879","practice_score":60.4,"elapsed_s":5.96,"path_efficiency":1.0}]
```

## Wound closure exercise

Contract: `docs/WOUND_CLOSURE.md`. Create with `{"trainee_id":"...","exercise":"wound_closure"}`; any exercise id other than `peg_transfer`, `wound_closure` or `lap_chole` is `422`.

Event kinds depend on the session's exercise. `peg_transfer` accepts `transfer_success`, `drop`, `invalid_release`. `wound_closure` accepts `stitch_placed`, `stitch_missed`, `tissue_contact`. A batch containing a kind from the other exercise is rejected whole with `422` `{"detail":"event kind drop not valid for exercise wound_closure"}`. Positions keep the same rule (finite, each coordinate within 10 m).

Metrics keep every field. For wound closure `successful_transfers` = stitches placed (a site scores once), `drops` = tissue contacts, `invalid_releases` = missed stitches, `completion_pct` = placed / 5 × 100. Three optional fields are added to every metrics object and are `null` for peg transfer:

| field | meaning |
|---|---|
| `exercise` | `"wound_closure"` (null for peg transfer) |
| `mean_bite_error_mm` | mean distance of each entry and exit hit from its site point, computed by the backend from `source`/`target` |
| `spacing_sd_mm` | SD of the gaps between consecutive placed stitches; null with fewer than 3 stitches |

The score formula, ideal path and thresholds are documented in `backend/scoring.py` and `backend/assessment.py` (prototype rubric, not clinical). Cost uses the same OR rates and error pricing; errors = missed + contacts; the instrument line is a suture and needle-driver set at an **assumed** $30 (`assumed: true`, no source). Coach drills for wound closure: `wound_closure`, `wound_closure_bite_accuracy`, `wound_closure_spacing`, `wound_closure_tissue_handling`. Assessment criteria: `completion`, `time`, `bite_accuracy`, `spacing`, `tissue_contacts`, `path_efficiency`, `bimanual_balance`. Progress rows gain `exercise`.

Live check on 2026-10-24: `python tools/fake_quest.py --base-url http://127.0.0.1:8770 --exercise wound_closure` against a temp SQLite DB, Gemini unset (fallback coach). Metrics returned:

```json
{"completion_pct":100.0,"successful_transfers":5,"drops":1,"invalid_releases":1,"elapsed_s":43.84,"distance_by_instrument":{"right":0.5015002423495984,"left":0.21683309724854896},"total_distance_m":0.7183333395981474,"path_efficiency":0.43972800300253484,"practice_score":82.5,"exercise":"wound_closure","mean_bite_error_mm":1.17,"spacing_sd_mm":2.14}
```

Cost total `$334.50` (including the assumed $30 suture line). Coach summary: `5 of 5 stitches in 44s, mean bite error 1.2 mm, 1 tissue contact and 1 missed. Practice score 82/100.`

## Laparoscopic cholecystectomy exercise

Contract: `docs/LAP_CHOLE.md`. Create with `{"trainee_id":"...","exercise":"lap_chole"}`. Accepted kinds: `step_completed`, `port_placed`, `insufflation`, `critical_view`, `clip_applied`, `structure_cut`, `ductotomy`, `cholangiogram`, `injury`; a batch with a kind from another exercise is `422` as for wound closure. Object ids are not validated; unknown step ids and structures are ignored by scoring.

Every event (all exercises) now takes two optional fields: `value` (finite number, else `422`) and `detail` (string, at most 64 characters, else `422`). Both are stored and returned by `GET /events` (`null` when absent). Peg-transfer and wound-closure scoring ignore them. An existing SQLite or Postgres `events` table gains the two columns at startup (`ALTER TABLE ... ADD COLUMN` when missing).

Safety rules, all decided by the backend from the sequence (`backend/scoring.py`, `lap_chole_findings`):

| severity | rule |
|---|---|
| major | any `injury`; any `clip_applied` or `structure_cut` on `cbd` |
| major | a cut of `cystic_duct` / `cystic_artery` without 2 earlier clips at least 2 mm proximal (smaller `value`) and 1 at least 2 mm distal (`unsafe_division`) |
| major | a cystic cut before the critical view was reached: `step_completed` `dissect` or `critical_view` `value` ≥ 100, whichever is first (`division_before_cvs`) |
| major | a `ductotomy` on anything but `cystic_duct` (`ductotomy_wrong_structure`; on `cbd` it also counts as a bile duct injury for cost), on the cystic duct without an earlier cystic-duct clip at a larger distance (`unsafe_ductotomy`), or before the critical view (`ductotomy_before_cvs`). A ductotomy is not a division |
| minor | a port more than 15 mm (distance `source`→`target`) or more than 20° (`|value|`) off; once per port |
| minor | final `insufflation` value outside 12–15 mmHg; `clip_applied` with `detail` `crossing`; a clip closer than 2 mm to a cut (once per clip); `cholangiogram` with `detail` `not_in_duct` or `leak` |

A crossing clip still counts toward the 2 + 1 rule. In the video's order the duct gets its distal clip (`clip_duct_distal`) before the ductotomy and cholangiogram, and that clip is the distal clip for `divide_duct`; the ductotomy sits between it and the cut. One cystic cut can be both unsafe and early (two major errors).

Steps (15, in the video's order): `veress`, `insufflate`, `port_umbilical`, `port_epigastric`, `port_rsc_medial`, `port_rsc_lateral`, `retract`, `dissect`, `clip_duct_distal`, `ductotomy`, `cholangiogram`, `clip_duct_proximal`, `divide_duct`, `clip_artery`, `divide_artery`.

Metrics: `successful_transfers` = steps completed (of 15, each id once), `completion_pct` = completed / 15 × 100, `drops` = major errors, `invalid_releases` = minor errors, path length from samples; ideal path = straight-line polyline through port sites, clip, ductotomy and cut positions in time order. `exercise` = `"lap_chole"`. New optional `details` (object of string → number, string or bool; `null` for the other exercises): `steps_completed`, `steps_total`, `ports_placed`, `ports_mean_error_mm`, `ports_max_error_mm`, `ports_mean_angle_deg`, `insufflation_mmhg`, `insufflation_in_range`, `critical_view_pct`, `critical_view_reached`, `clips_cystic_duct`, `clips_cystic_artery`, `crossing_clips`, `safe_divisions`, `unsafe_divisions`, `divisions_before_cvs`, `ductotomies`, `unsafe_ductotomies`, `cholangiogram_depth_mm`, `cholangiogram_result`, `cholangiogram_errors`, `cbd_errors`, `injuries`, `clipping_errors`, `bile_duct_injury`, `major_errors`, `minor_errors`. Measurement keys are left out when nothing was reported.

Practice score (prototype rubric, not clinical): `100 × (0.35 completion + 0.15 port accuracy + 0.10 insufflation in range + 0.10 critical view % + 0.10 path efficiency + 0.10 time factor (600 s budget) + 0.10 minor-error factor (budget 5))`; **any major error caps the score at 40**.

Cost: OR time lines as before; `Laparoscopic instruments (reusable|disposable)` from Apelgren 1994 (sourced); `Hem-o-lok clips and trocars (assumed price)` at an **assumed** $150 (`assumed: true`, no source; may overlap the disposable set); `Expected cost of major errors` = major errors × assumed risk × $13,857 (minor errors are not priced); when a CBD clip, cut, ductotomy or `cbd` injury occurred, `Bile duct injury (assumed: priced at the general-surgery complication cost)` = $19,626 (JAMA Surg 2016 figure; applying it to a bile duct injury is an assumption, `assumed: true`). Progress rows: `errors` = major + minor, `cost_total_usd` as `GET /cost`.

Coach drills: `lap_chole`, `lap_chole_port_placement`, `lap_chole_critical_view`, `lap_chole_clipping`. The coach error list for lap chole is the findings above (each with `code` and `severity`, majors first); the Gemini prompt names the exercise and its 15 steps. Assessment criteria: `completion`, `time` (rated only when all 15 steps are done), `port_accuracy` (mm), `insufflation` (mmHg outside 12–15), `critical_view` (%), `clipping_safety` (count), `major_errors` (any is `needs_work`), `path_efficiency`.

Convex mirror: events carry `value` / `detail` only when present; session metrics carry `details` only when present.

Live check on 2026-10-24 (15-step order): a second uvicorn on port 8771 with a temp SQLite DB, Gemini and Convex unset (fallback coach). `python tools/fake_quest.py --base-url http://127.0.0.1:8771 --exercise lap_chole`: 15/15 steps, 0 major, 1 minor (crossing clip), ports 6.24 mm mean, 13.5 mmHg, critical view 100%, 1 ductotomy, cholangiogram `ok`, 2 safe divisions, path efficiency 0.28, score 82.1, cost $285.16. Coach: `15 of 15 steps in 138s, 0 major errors and 1 minor, critical view 100% cleared, ports 6.2 mm off on average, insufflation 13.5 mmHg. Practice score 82/100.` With `--lap-chole-unsafe` (first proximal duct clip on the CBD): 2 major (CBD clip, unsafe duct division), 1 minor, score 40.0, cost $20,188.30 including the assumed bile duct injury line; first tip `Clip placed across the common bile duct at 103.7s. Major error: clip only the cystic structures.`

## Commands

These samples are from `backend/tests/test_commands.py` against FastAPI `TestClient` on 2026-10-24 (in-memory SQLite). Commands live in SQLite; Convex is a read-only mirror and is not read here. The Quest `CommandPoller` polls every 0.75 s by default.

The dashboard (`dashboard/src/api.ts`) sends `highlight_target` (targets `target_1`…`target_6`; peg N belongs on target N), `request_pause` and `request_retry`, each with a `dash_<uuid>` id and a 5-minute expiry. Instructor notes arrive as `add_annotation` (see annotations above). `assign_exercise` is accepted but only `peg_transfer` exists in Unity.

Create, `POST /api/v1/sessions/{id}/commands`:

`201`

```json
{"commandId":"cmd_018","type":"highlight_target","targetId":"target_2","expiresAt":"2026-10-24T18:00:00Z","message":"","exerciseId":""}
```

The same body again is `200` with that JSON. A different body for `cmd_018` is `409` `{"detail":"command already exists"}`. Types are `highlight_target`, `add_annotation`, `request_pause`, `request_retry`, and `assign_exercise`.

Pending poll, `GET /api/v1/sessions/{id}/commands`:

`200`

```json
{"commands":[{"commandId":"cmd_018","type":"highlight_target","targetId":"target_2","expiresAt":"2026-10-24T18:00:00Z","message":"","exerciseId":""}]}
```

Ack, `POST /api/v1/sessions/{id}/commands/cmd_018/ack` with `{"status":"applied","message":"Applied locally"}`:

`200`

```json
{"status":"applied","message":"Applied locally"}
```

A later ack with a different status stays `200` and returns the first acknowledgement. The pending poll skips commands whose `expiresAt` has passed, so stale commands never crowd out new ones (the poll returns at most 100).

History, `GET /api/v1/sessions/{id}/commands/history`: the newest 50 commands, each with the fields above plus `state` (`queued`, `applied`, `rejected`, or `expired`), `ackMessage` (the headset's reason, e.g. `Unknown target`) and `createdAt`. The dashboard polls it every 3 s to show delivery state, and uses Convex only if this request fails. The next poll is `{"commands":[]}`. A missing session is `404` `{"detail":"session not found"}`. An unknown command on a real session is `404` `{"detail":"command not found"}`.

## Status codes from the same run

Unknown session, `GET /api/v1/sessions/missing`:

`404`

```json
{"detail":"session not found"}
```

`POST /complete` on a session still in `ready` (`ebfaf59bf39347fa8ccfd907c30a1e5d`):

`409`

```json
{"detail":"cannot complete from ready"}
```

`GET /cost` and `POST /coach` on that same uncompleted session:

`409`

```json
{"detail":"session not completed"}
```
