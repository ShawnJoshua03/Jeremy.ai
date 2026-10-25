# Contract: laparoscopic cholecystectomy CVS trainer and backend (v1, frozen)

The operation is a **laparoscopic cholecystectomy** (gallbladder removal). This file is the v1 contract for the Critical View of Safety trainer. The session exercise id is `lap_chole_cvs`. Workers code against this file. If it must change, say so in the summary. Do not silently diverge.

Branch base: `integration`. The Unity app is `quest/Assets/GallbladderTrainer/` (identify the cystic duct, then judge two prepared views). `quest/Assets/Scripts/` is deleted. The old robot avatar is in git history at commit `868bafcf` (`quest/Assets/Scripts/Feedback/RobotNarrator.cs`), for reference only.

Stable IDs are in `GallbladderTrainer/Runtime/TrainingModels.cs`. Do not rename them.

| Kind | IDs |
|---|---|
| Structures | `gallbladder`, `cystic_duct`, `cystic_artery`, `cystic_plate` |
| Decisions | `criteria_demonstrated`, `criteria_not_demonstrated`, `request_instructor` |
| Exercises | `ex_identify` (target `cystic_duct`), `ex_view_a` (expected `criteria_not_demonstrated`), `ex_view_b` (expected `criteria_demonstrated`) |
| Modes | `explore`, `assess`, `review` |

## How to change it safely

v1 is frozen.

- IDs above are stable. Do not rename a structure, decision, exercise, mode, or event kind.
- Add fields. Do not rename or remove a field, an enum value, or an event kind in v1.
- A breaking change bumps the version. Leave v1 in place until clients have moved. Put the new shapes in a v2 section of this file in the same change as the code.
- The golden fixtures `backend/tests/fixtures/cvs_*.json` must keep passing. They lock a full session: samples, events, the deterministic score, and the report numbers. A contract edit that fails them is not done.
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

The backend recomputes correctness from its own copy of the exercise table. That result is the source of truth.

## Backend outputs

Narrator routes, also used for `lap_chole_cvs`:

- `POST /sessions/{sid}/narrator/line` returns `{text, step, expected_progress, actual_progress, delta, severity, repeat}`. `severity` is `on_track`, `slow`, `off_track`, or `critical`.
- `POST /sessions/{sid}/narrator/line/audio` returns `audio/wav`. HTTP 503 means speech is unavailable. The client shows the text.

For `lap_chole_cvs`, `step` is the current exercise id. The pace target is 75 seconds per exercise. A wrong decision is `off_track`. Calling a view "criteria demonstrated" when it is not (`ex_view_a`) is `critical`. That call is the classic setup for a bile duct injury. Requesting the instructor is good behaviour and is never penalised.

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

All numbers are computed deterministically. Gemini, with thinking disabled (`backend/coach.py`), writes only `review`, `tip`, `summary`, `strengths`, and `focus_next` from those facts. Output that contains a number that is not in the facts is discarded and the template is used.

- `POST /sessions/{sid}/report/audio` returns `audio/wav` of `summary` via Cartesia. HTTP 503 means text only.
- `GET /sessions/{sid}/replay` returns `{duration_ms, tracks: {left: [[t_ms, x, y, z], ...], right: [...], head: [...]}, events: [{t_ms, kind, object_id, detail}]}`. Tracks are downsampled to at most 10 Hz, for the in-headset replay and the dashboard replay.

Mistake cost uses the existing routes: `mistake-cost/summary` is free, `mistake-cost/report` is x402. For `lap_chole_cvs`:

- `ex_view_a` with a false "demonstrated" adds a bile duct injury risk line.
- `ex_identify` with the wrong structure adds a misidentification line, priced with the same adverse-event assumption.
- `request_instructor` is $0.

Figures are illustrative. They are not billing data. The per-error adverse-event probability is an assumption. See [COST_DATA.md](COST_DATA.md).
