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

## POST /api/v1/sessions

```powershell
curl.exe -s -X POST http://127.0.0.1:8765/api/v1/sessions -H "Content-Type: application/json" --data-binary "@session.json"
```

`session.json` was `{"trainee_id":"judge","exercise":"peg_transfer"}`.

`201`

```json
{"id":"5f5ea34168e148f9bce017745c9ec128","trainee_id":"judge","exercise":"peg_transfer","status":"ready","created_at":"2026-10-24T08:10:22.783397Z","started_at":null,"completed_at":null,"elapsed_s":null}
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
