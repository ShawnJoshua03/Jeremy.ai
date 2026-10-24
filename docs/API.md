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

`API_KEY` is optional. When it is unset or blank, `/api/v1` behaves as before. When it is set, every `/api/v1` route requires the header `X-API-Key` to equal that value. A missing or wrong key is `401` `{"detail":"invalid api key"}`. `GET /health` stays open. `OPTIONS` preflight does not require the key, so the browser can send `X-API-Key` on the real request. A `401` from an allowed origin still includes `Access-Control-Allow-Origin`.

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

Dashboard session picker. Query parameters: `trainee_id`, `status` (`ready`, `running`, or `completed`), and `limit` (default 50, minimum 1, maximum 100). A blank `trainee_id` or `status` is ignored. An unknown status is `422`. `limit` outside 1–100 is `422`. A trainee with no sessions returns `[]`. Rows are newest `created_at` first, then `id`.

Checked with FastAPI `TestClient` on 2026-10-24 (in-memory SQLite). Not captured with curl. SQLite dropped the trailing `Z`.

`200` for `?trainee_id=ada&status=running&limit=1` after create and start:

```json
[{"id":"22f849dcaeee4874b3b87249b23e2bd0","trainee_id":"ada","exercise":"peg_transfer","status":"running","created_at":"2026-10-24T09:40:37.794265","started_at":"2026-10-24T09:40:37.820000","completed_at":null,"elapsed_s":null}]
```

Unknown status `paused`:

`422`

```json
{"detail":"status must be ready, running, or completed"}
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

Create is `POST /api/v1/sessions/{id}/annotations` with `{"at_ms":90,"text":"watch the drop","author":"coach"}` (`201`). The read after that post:

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

Only after complete. This run had no Gemini key, so `source` is `fallback`. A live Gemini response was **not verified**.

```powershell
curl.exe -s -X POST http://127.0.0.1:8765/api/v1/sessions/5f5ea34168e148f9bce017745c9ec128/coach
```

`200`

```json
{"summary":"1 of 6 transfers in 6s with 1 errors. Practice score 60/100.","tips":[{"text":"A drop at 0.0s. Slow down and confirm the grasp before moving.","event_id":"drop-1"}],"source":"fallback"}
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

## Commands

These samples are from `backend/tests/test_commands.py` against FastAPI `TestClient` on 2026-10-24 (in-memory SQLite). They were not captured with curl. Convex is not read. The headset poller stays off unless `pollingEnabled` is set.

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

A later ack with a different status stays `200` and returns the first acknowledgement. The next poll is `{"commands":[]}`. A missing session is `404` `{"detail":"session not found"}`. An unknown command on a real session is `404` `{"detail":"command not found"}`.

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
