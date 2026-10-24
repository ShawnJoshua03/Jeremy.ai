# Scoring

How Jeremy.ai scores an attempt, and where AI is and is not allowed. The code is the source of truth: `backend/scoring.py` (metrics and practice score) and `backend/assessment.py` (per-criterion levels). If this page and the code disagree, the code wins and this page is wrong.

## Principles

- **Deterministic.** The same events and samples always give the same metrics and score. No model, no randomness, no network call.
- **AI never scores.** Gemini receives metrics and explains them. It cannot create, change or override a score, a level or a finding. The backend validates every event ID and rule ID the coach cites.
- **Prototype, not clinical.** Every weight, budget and threshold is a demo choice. None is an FLS standard or a clinically validated cut-off. The practice score is not a probability of surgical success and must not be used to certify anyone.
- **Measured, then explained, then reviewed.** The scorer measures, the coach explains, and the instructor reviews.

## Pipeline

```
Quest events + instrument samples
  -> POST /sessions/{id}/complete
  -> scoring.compute_for_exercise(exercise, samples, events, elapsed_s)  -> Metrics (stored)
  -> assessment.assess(metrics)                                          -> levels per criterion
  -> cost.py (illustrative $), coach.py (Gemini explanation), dashboard
```

The score is computed once at completion and stored. `GET /assessment` and the coach read the stored metrics and never rescore.

## Shared inputs

- **Sample:** `instrument` (`left`/`right`), `index`, `t_ms`, `pos` (metres), `valid`.
- **Event:** `kind`, `t_ms`, optional `instrument`, `object_id`, `source`, `target`, `value`, `detail`, `event_id`. Allowed kinds per exercise are in `EVENT_KINDS`.

## Shared metrics

**Travel distance** per instrument is the sum of straight-line steps between consecutive valid samples, ordered by `index`. An invalid sample breaks the path. A gap over `MAX_GAP_MS` (250 ms) is not bridged, so tracking loss never adds fake distance.

**Path efficiency** = `clamp(ideal / travelled, 0, 1)`. It is 0 with no tracked motion. "Ideal" is exercise-specific (below).

**Time factor** = `clamp(1 - elapsed_s / budget, 0, 1)`.

**Error factor** = `clamp(1 - errors / budget, 0, 1)`.

All exercises return the same `Metrics` fields so older clients still parse them. Exercise-specific meaning is mapped onto `successful_transfers`, `drops` and `invalid_releases` as listed below.

## Peg transfer

| Item | Value |
|---|---|
| Objects | 6 |
| Completion | unique `transfer_success` objects / 6. An object scores once. |
| Errors | `drop` + `invalid_release`, budget 6 |
| Ideal path | sum of straight-line source → target distances of scored transfers |
| Time budget | 300 s |

```
score = 100 * (0.45*completion + 0.25*path_efficiency + 0.20*time_factor + 0.10*error_factor)
```

## Wound closure

See [WOUND_CLOSURE.md](WOUND_CLOSURE.md) for the event contract. Geometry is board-local metres and must match Unity's `WoundPad`.

| Item | Value |
|---|---|
| Sites | 5. A site scores once (first `stitch_placed`). |
| Bite error | distance of each entry and exit hit from its site point, in mm. `mean_bite_error_mm` averages all bites. |
| Bite accuracy | `clamp(1 - mean_bite_error_mm / 8, 0, 1)`, 0 with no stitches |
| Spacing | population SD of gaps between placed stitch centres (`spacing_sd_mm`), null under 3 stitches. Reported, not in the score. |
| Errors | `stitch_missed` + `tissue_contact`, budget 5 |
| Ideal path | bite lengths + hands × travel from each exit to the next entry |
| Time budget | 300 s |

```
score = 100 * (0.40*completion + 0.20*bite_accuracy + 0.15*path_efficiency
               + 0.15*time_factor + 0.10*error_factor)
```

Mapping: `successful_transfers` = stitches placed, `drops` = tissue contacts, `invalid_releases` = missed stitches.

## Laparoscopic cholecystectomy

See [LAP_CHOLE.md](LAP_CHOLE.md) for the event contract. Events carry both actual and ideal positions, so no anatomy lives in the backend.

**Findings** (`lap_chole_findings`) are the single source for error counts, the coach and cost.

| Severity | Code | Trigger |
|---|---|---|
| major | `injury` | any `injury` event |
| major | `cbd_clip`, `cbd_cut` | a clip or cut on the common bile duct |
| major | `unsafe_division` | cystic duct or artery cut without ≥ 2 proximal and ≥ 1 distal clips, each ≥ 2 mm from the cut and applied before it |
| major | `division_before_cvs` | cystic cut before the critical view of safety was reached |
| minor | `port_offset` | port > 15 mm from its site or > 20° off |
| minor | `insufflation_out_of_range` | final pressure outside 12–15 mmHg |
| minor | `clip_crossing` | a clip with `detail: "crossing"` |
| minor | `clip_too_close` | a clip < 2 mm from the cut on the same structure |

Critical view reached = the earlier of `step_completed` `dissect` and a `critical_view` event with value ≥ 100.

| Component | Weight | Definition |
|---|---|---|
| completion | 0.35 | unique known steps completed / 11 |
| port_accuracy | 0.15 | mean of `clamp(1 - 0.5*err_mm/15 - 0.5*angle/20, 0, 1)` |
| insufflation_ok | 0.10 | 1 if final pressure in 12–15 mmHg |
| critical_view | 0.10 | max `critical_view` value / 100 |
| path_efficiency | 0.10 | ideal = polyline through port, clip and cut positions in time order |
| time_factor | 0.10 | budget 600 s |
| minor_factor | 0.10 | budget 5 minor errors |

**Any major error caps the score at 40.** A fast, tidy attempt that cuts the bile duct must not look like a pass.

Mapping: `successful_transfers` = steps completed, `drops` = major errors, `invalid_releases` = minor errors. Exercise-specific numbers go in `details`.

## Assessment levels

`assessment.py` turns stored metrics into `proficient`, `developing`, `needs_work` or `not_assessed` per criterion. All cut-offs are in `THRESHOLDS` and `WOUND_CLOSURE_THRESHOLDS`, and nowhere else.

| Exercise | Criteria |
|---|---|
| Peg transfer | completion, time, errors, path_efficiency, bimanual_balance |
| Wound closure | completion, time, bite_accuracy, spacing, tissue_contacts, path_efficiency, bimanual_balance |

- **Bimanual balance** = shorter / longer instrument travel distance.
- **Time** is `not_assessed` unless the attempt was completed. A fast but unfinished run is not a fast run.
- A criterion with no data (no tracked motion, no stitches, fewer than 3 stitches for spacing) is `not_assessed` with value `null`.
- Smoothness is not reported because the scorer does not compute it.
- **Lap chole has no assessment thresholds yet.** `exercise_of` falls back to peg-transfer criteria for it, which is wrong for that exercise. Until thresholds are added, show lap chole findings and `details` instead of levels.

## Where vision models (VLMs) fit

A Gemini Flash vision model could look at headset frames. It is **not** part of scoring and must not become part of it:

- The sim already has exact geometry and event order. A VLM estimate from frames would be a noisier copy of numbers the scorer already has.
- VLM judgments are not reproducible across runs, so they cannot back a score a trainee can dispute.
- Cost is computed from scorer output, so a fuzzy score would make cost fuzzy too.
- General VLMs are not validated for surgical skill assessment. Any accuracy claim needs a labeled test set first.

Allowed future uses, all **advisory and outside `practice_score`**:

1. **Observations the event log cannot see**, such as a real-hand grip or two-handed technique on the physical phantom through passthrough. Depends on Quest 3 passthrough camera access, which is **not verified**.
2. **Coach context.** Fixed-vocabulary labels (for example `handoff_hesitation`) added next to the metrics in the coaching request. The backend rejects any label not on the list.
3. **Instructor flags.** Shown on the dashboard as "AI observation, unverified" with a timestamp. Instructor confirm or dismiss decisions become labeled data for later evaluation.

Latency is seconds per call, so vision runs only at phase end or attempt end, never in the local safety and feedback loop. Frames go Quest → backend → Gemini. The API key stays on the backend.

## Changing the rubric

1. Change the constant or weight in `backend/scoring.py` or `backend/assessment.py` only.
2. Update the docstring or comment block next to it and this page.
3. Update tests in `backend/tests/` and run `.\.venv\Scripts\python -m pytest -q`.
4. Keep wound-closure geometry identical in Unity's `WoundPad` and `scoring.py`.
5. Keep "prototype, not clinically validated" on every score shown to users.
