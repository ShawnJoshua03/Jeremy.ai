# Contract: laparoscopic cholecystectomy CVS trainer and backend (v1, frozen)

## Important update (read first)

Atharv's GallbladderTrainer push is a **starter view**. The operation is a **laparoscopic cholecystectomy** (gallbladder removal). Dissection, clipping, cutting, and the other procedure steps are not in this push. They will arrive later and can change models and add exercises.

- The three starter exercises live in **one** data table. Backend: one dict or table. Unity: read `ExerciseLibrary` at runtime. No switch on exercise ids, in new code or in the bridge.
- The bridge, recorder, guide, report, and replay are generic. Anything raised through `TrainingEvents` or `ITrainingBackendHook` is recorded and shown. An unknown exercise id or structure id is passed through. It is stored, replayed, and listed. It scores as not assessed (`correct: null`). It is not a wrong answer, and it does not drop the event or crash the session.
- Reserve the existing procedure event kinds `step_completed`, `clip_applied`, `structure_cut`, `injury`, and `critical_view` for the steps still to come. A `lap_chole_cvs` session accepts them now. Replay and the report include them even when no scoring rule exists yet.
- New Unity code stays in its own folders so Atharv's later files merge cleanly. Edits to his files stay minimal.

Workers code against this file. If it must change, say so in the summary. Do not silently diverge.

Branch base: `integration`. The Unity app is `quest/Assets/GallbladderTrainer/` (starter view: identify the cystic duct, then judge two prepared views). `quest/Assets/Scripts/` is deleted. The old robot avatar is in git history at commit `868bafcf` (`quest/Assets/Scripts/Feedback/RobotNarrator.cs`), for reference only.

The session exercise id is `lap_chole_cvs`. Stable IDs for the starter view are in `GallbladderTrainer/Runtime/TrainingModels.cs`. Do not rename them. Later exercises and structures are new ids in the same table. They are not a new session exercise id.

| Kind | IDs |
|---|---|
| Structures | `gallbladder`, `cystic_duct`, `cystic_artery`, `cystic_plate` |
| Decisions | `criteria_demonstrated`, `criteria_not_demonstrated`, `request_instructor` |
| Exercises | `ex_identify` (target `cystic_duct`), `ex_view_a` (expected `criteria_not_demonstrated`), `ex_view_b` (expected `criteria_demonstrated`) |
| Modes | `explore`, `assess`, `review` |

## How to change it safely

v1 is frozen.

- IDs in the starter table are stable. Do not rename a structure, decision, exercise, mode, or event kind.
- Add exercises, structures, and fields in the one table. Do not rename or remove a field, an enum value, or an event kind in v1. Do not hard-code the starter ids anywhere except that table.
- Unknown ids stay in the pipeline with `correct: null`. A new scoring rule is a new row in the table, not a new branch in the recorder, the guide, the report, or the replay.
- A breaking change bumps the version. Leave v1 in place until clients have moved. Put the new shapes in a v2 section of this file in the same change as the code.
- The golden fixtures `backend/tests/fixtures/cvs_*.json` must keep passing. They lock a starter session and a session that carries unknown ids plus the reserved procedure events. A contract edit that fails them is not done.
- Those fixture files are not in this tree yet (checked 2026-10-24). Until they exist, this doc is the lock. The first implementation adds them and keeps them green.
- The backend recomputes correctness, the score, and every dollar figure. Gemini only narrates numbers the backend already computed.
- Update this file in the same change as the code.

## Session lifecycle

Existing endpoints. Base path `/api/v1`.

1. `POST /sessions` with `{trainee_id, exercise: "lap_chole_cvs", client_request_id}` returns `{id, ...}`.
2. `POST /sessions/{sid}/start`
3. `POST /sessions/{sid}/samples` with `{samples: [...]}`. Movement, batched about every 1 second.
4. `POST /sessions/{sid}/events` with `{events: [...]}`. Sent as they happen, batched at or under 250 ms.
5. `POST /sessions/{sid}/complete` with `{elapsed_s}`

Auth is an optional `X-API-Key` header, the same as the other session routes.

## Samples (movement), 20 Hz

```text
{instrument: "left"|"right"|"head", idx, t_ms, pos: [x, y, z], quat: [x, y, z, w], valid, interaction: "idle"|"pointing"|"selecting"}
```

`pos` is metres in workstation-local space, relative to the anatomy workstation root, so a recording survives re-centering and later model changes. `t_ms` is milliseconds since session start. `left` and `right` are the controllers or tracked hands. `instrument: "head"` is new. The backend must accept it.

## Events

New kinds for exercise `lap_chole_cvs`. Common fields: `event_id` (unique), `kind`, `t_ms`.

| Kind | Fields |
|---|---|
| `exercise_started` | `object_id` = exercise id |
| `mode_changed` | `detail` = mode |
| `structure_hovered` | `object_id` = structure id, `value` = dwell seconds. Sent on hover end, and only if the dwell is at least 0.3 s |
| `structure_selected` | `object_id` = structure id, `detail` = exercise id |
| `decision_submitted` | `object_id` = exercise id. `detail` = decision id for the CVS exercises, or `identify:<structure id>` for `ex_identify`. `value` = client result (1 match, 0 mismatch, 0.5 unclear). Informational only |
| `instructor_requested` | `object_id` = exercise id |
| `step_completed`, `clip_applied`, `structure_cut`, `injury`, `critical_view` | Reserved for later procedure steps. Same common fields, plus the fields those kinds already use on `lap_chole`. Accepted on `lap_chole_cvs` now. Included in replay and the report. No score until a table row says so |

The backend recomputes correctness from its own copy of the exercise table. That result is the source of truth. An id missing from the table is not assessed (`correct: null`). The client `value` does not decide it.

## Backend outputs

Narrator routes, also used for `lap_chole_cvs`:

- `POST /sessions/{sid}/narrator/line` returns `{text, step, expected_progress, actual_progress, delta, severity, repeat}`. `severity` is `on_track`, `slow`, `off_track`, or `critical`.
- `POST /sessions/{sid}/narrator/line/audio` returns `audio/wav`. HTTP 503 means speech is unavailable. The client shows the text.

For `lap_chole_cvs`, `step` is the current exercise id, read from the table, including an id the table does not know yet. The pace target is 75 seconds per exercise. A wrong decision on a known row is `off_track`. Calling a view "criteria demonstrated" when that row expects otherwise (starter row `ex_view_a`) is `critical`. That call is the classic setup for a bile duct injury. An unknown exercise is not `off_track` and not `critical`. Requesting the instructor is good behaviour and is never penalised. Procedure events with no scoring row are spoken only as recorded facts. They do not invent a score.

Review report, new. `GET /sessions/{sid}/report` after complete. HTTP 409 before complete.

```text
{session_id, exercise, trainee_id, completed_at,
 score: 0-100,
 grade: "excellent"|"competent"|"needs_practice",
 exercises: [{exercise_id, title, decision, expected, correct: bool|null, unclear: bool,
              time_to_decision_s, structures_inspected: [ids in order], hover_dwell_s: {id: s},
              hand_path_m, head_path_m, hesitation_s, review: "1-2 sentences", tip: "one sentence"}],
 movement: {hand_path_m, head_path_m, smoothness: 0-1, idle_s, samples: n},
 summary: "3-4 sentence plain-language review", strengths: [...], focus_next: [...],
 mistake_cost: {total_usd, lines: [{label, usd, source, assumed}], report_url, paid_detail_requires_x402: true},
 source: "gemini"|"fallback", disclaimer}
```

`exercises` lists every exercise id seen in the session, starter rows and unknown ids. Unknown ids use `correct: null` and still carry the recorded decision, dwell, and paths. Reserved procedure events appear in the report even when they have no score line.

All numbers are computed deterministically. Gemini, with thinking disabled (`backend/coach.py`), writes only `review`, `tip`, `summary`, `strengths`, and `focus_next` from those facts. Output that contains a number that is not in the facts is discarded and the template is used.

- `POST /sessions/{sid}/report/audio` returns `audio/wav` of `summary` via Cartesia. HTTP 503 means text only.
- `GET /sessions/{sid}/replay` returns `{duration_ms, tracks: {left: [[t_ms, x, y, z], ...], right: [...], head: [...]}, events: [{t_ms, kind, object_id, detail}]}`. Tracks are downsampled to at most 10 Hz, for the in-headset replay and the dashboard replay.

Mistake cost uses the existing routes: `mistake-cost/summary` is free, `mistake-cost/report` is x402. For `lap_chole_cvs`:

- `ex_view_a` with a false "demonstrated" adds a bile duct injury risk line.
- `ex_identify` with the wrong structure adds a misidentification line, priced with the same adverse-event assumption.
- `request_instructor` is $0.

Figures are illustrative. They are not billing data. The per-error adverse-event probability is an assumption. See [COST_DATA.md](COST_DATA.md).
