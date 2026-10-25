# CVS trainer scoring (`lap_chole_cvs`)

The laparoscopic cholecystectomy critical view of safety (CVS) trainer. Code: `backend/cvs_scoring.py`. This is a prototype rubric. It is **not** a validated assessment of surgical competence. Every number here is deterministic, and Gemini never produces a score.

## Exercise table

`cvs_scoring.EXERCISES` is the backend's own copy of the Unity `ExerciseLibrary`, and it is the only place exercise ids appear in the code. To add an exercise, add a row.

| id | kind | correct answer |
|---|---|---|
| `ex_identify` | identify | `identify:cystic_duct` |
| `ex_view_a` | cvs | `criteria_not_demonstrated` |
| `ex_view_b` | cvs | `criteria_demonstrated` |

The backend recomputes correctness from this table. The client `value` on `decision_submitted` is informational only.

- **Unknown exercise ids** are recorded, reported and replayed with `correct: null`. They are never scored as wrong.
- **Unknown decision ids** on a cvs exercise are also `correct: null`.
- **Identify exercises:** any structure other than the target is wrong (`misidentification`), whether or not its id is known. For example, `common_hepatic_duct` is a real mesh in the model.
- **Reserved procedure kinds** (`step_completed`, `clip_applied`, `structure_cut`, `injury`, `critical_view`) are accepted, stored and replayed. They are not scored yet.

## Per exercise

**Segments.** Each `exercise_started` opens a segment that ends at the next `exercise_started`, or at the session end (the latest of `elapsed_s`, the last sample and the last event).

**Graded decision.** This is the *first* `decision_submitted` for the exercise whose detail is not `request_instructor`. A retry after a mismatch is recorded but does not change the grade.

| field | rule |
|---|---|
| `correct` | `true` if the graded decision matches the table. `false` with an error code otherwise: `cvs_false_positive` means "criteria demonstrated" was called on a view whose answer is "not demonstrated"; `wrong_decision` and `misidentification` cover the other errors. `null` if not assessed. |
| `unclear` | `true` if there is any `request_instructor` decision or an `instructor_requested` event for the exercise. |
| `decision` | The graded decision. If there is none, `request_instructor` when the exercise is unclear, else `null`. |
| `time_to_decision_s` | Graded decision time minus the exercise's first `exercise_started`. `null` without both. |
| `structures_inspected` | Structure ids in order of first `structure_hovered` or `structure_selected`. A selection is attributed by its `detail` (exercise id); a hover is attributed by its segment. |
| `hover_dwell_s` | Sum of the `structure_hovered` values per structure. |
| `hesitation_s` | `max(0, (graded decision - first structure_selected) / 1 s - 5)`, and 0 without a selection. |
| `hand_path_m`, `head_path_m` | Travel distance of valid `left`/`right` (or `head`) samples inside the exercise's segments. The path is not bridged across gaps over 250 ms or across invalid samples (same rule as `scoring.travel_distance`). |

## Movement (whole session)

- **Path length:** `hand_path_m` and `head_path_m` use the same travel distance as above.
- **`smoothness`** = `clamp(1 - mean|jerk| / 50 m/s³, 0, 1)`.
  - Jerk comes from third finite differences over consecutive valid hand samples (gaps of 250 ms or less), and the mean is weighted by time.
  - The 50 m/s³ reference is a prototype choice.
  - The value is `null` when there are no hand samples.
- **`idle_s`** = 0.5 s × the number of half-second bins with tracked hand steps in which every hand moved slower than 0.02 m/s. A step counts in the bin of its later sample.

## Score (0-100) and grade

Over the graded exercises (those with `correct` not null):

```
accuracy     = correct / graded                         (0 if nothing graded)
pace         = mean clamp(1 - max(0, t_decision - 75) / 75, 0, 1)   full credit within 75 s, 0 at 150 s
decisiveness = mean clamp(1 - hesitation_s / 15, 0, 1)
score = 100 * (0.60*accuracy + 0.15*pace + 0.10*decisiveness + 0.15*smoothness) / (sum of the weights used)
```

- If `smoothness` is null, its weight is dropped rather than scored as 0.
- Any `cvs_false_positive` caps the score at **40**.
- **Grade:** `excellent` at 85 or above with no false positive, `competent` at 65 or above, otherwise `needs_practice`.

**Requesting the instructor is never penalised.** An exercise resolved only by `request_instructor` drops out of the graded set, and an exercise where the trainee asked first and then decided is graded on that decision.

## Stored metrics (`/complete`)

`lap_chole_cvs` reuses the shared metric fields:

| field | value |
|---|---|
| `practice_score` | the score |
| `successful_transfers` | correct decisions |
| `drops` | false "criteria demonstrated" calls |
| `invalid_releases` | misidentifications |
| `completion_pct` | known exercises resolved (decided or instructor requested) / 3 |
| `path_efficiency` | 1.0, because a decision exercise has no ideal path, so `/cost` charges no inefficient OR time |
| `details` | flat scalars: grade, counts, path lengths, idle, sample count, and smoothness when measured |

## Narrator (`POST /narrator/line`)

- `step` is the current exercise id: the latest `exercise_started`, or the first unresolved table exercise once that one is decided. It is `complete` when every table exercise is resolved.
- `expected_progress` = `elapsed / (75 s × exercises)`.
- `actual_progress` = resolved / exercises.

Severity:

| severity | when |
|---|---|
| `critical` | A false "criteria demonstrated" call within the last 75 s, or while that exercise is current. |
| `off_track` | Any other wrong decision, under the same window. |
| `slow` | 75 s or more since the current exercise started, or since the last decision if it has not started. |

A request for the instructor within the last 75 s gives an encouraging cue and is never a fault. Every line carries `provider: {text: "gemini"|"fallback"}`.

## Mistake cost

Illustrative, not billing data. The 0.01 probability is an assumption; see `docs/COST_DATA.md`.

| finding | line |
|---|---|
| False "criteria demonstrated" call | **Bile duct injury risk** = 0.01 × $19,626 general-surgery complication cost = **$196.26** (assumed) |
| Wrong structure on identify | **Misidentification** = 0.01 × $13,857 intraoperative adverse-event cost = **$138.57** (assumed) |
| Instructor requested | **$0** line |

There are no OR-time lines. A false negative on `ex_view_b` is not priced. `GET /report` shows these lines, and `mistake-cost/report` (x402) explains them.

## Report and replay

`GET /report` (409 before completion, or for other exercises) returns these numbers plus prose.

- **Prose source.** Gemini writes only `review`, `tip`, `summary`, `strengths` and `focus_next`, from the facts with money removed. Thinking is disabled and there is one 6 s request.
- **Fallback.** The deterministic template is used when Gemini is off or fails, when the output is malformed, or when it contains any number that is not in the facts (as given, or rounded to 0-2 decimals).
- **Caching.** The report is cached per session.
- **Provider fields.** `providers` and `provider_events` are recomputed on each GET from process-local activity in `backend/provider_log.py`, so they reset when the process restarts.

`GET /replay` returns valid samples per instrument downsampled to at most 10 Hz, plus every event. It works for any exercise and any status.
