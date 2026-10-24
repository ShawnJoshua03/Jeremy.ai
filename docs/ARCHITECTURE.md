# Architecture

**Unity local simulation + one FastAPI backend + deterministic rules + Gemini expert tutor + Cartesia speech + Convex mirror + React instructor dashboard.**
Scenario: **laparoscopic cholecystectomy, step 1 — port placement** (trocar entry and placement on an AR-overlaid abdomen). Peg transfer stays as the existing warm-up scenario. The practice experience runs locally on the headset and never waits on cloud latency. Moss is not used; Cartesia handles voice.

```
┌──────────────────────────────────────────────────────┐
│ META QUEST 3 (UNITY, passthrough)                    │
│  PortPlacementRig: virtual abdomen registered over   │
│   a physical mannequin/torso (or tabletop phantom)   │
│  ├── Landmarks: umbilicus, xiphoid, costal margins,  │
│   │   anterior axillary lines, pubic symphysis       │
│  ├── Tools: Veress/entry, trocar + cannula, camera   │
│  ├── Port-placement state machine                    │
│  ├── Local geometry check + Tier 0 safety cues       │
│  ├── Event recorder (port geometry, order, timing)   │
│  └── Coach UI + audio playback                       │
│  Local simulation continues without cloud access     │
└───────────────────────┬──────────────────────────────┘
                        │ HTTPS: batched events, ~1 Hz state, questions
                        ▼
┌──────────────────────────────────────────────────────┐
│ FASTAPI BACKEND (VULTR), one application             │
│  Session API · Event ingestion · Command API         │
│              ▼                                       │
│  Scenario plugins (peg_transfer, port_placement)     │
│  ├── Deterministic port-placement scorer             │
│  ├── Rule engine (literature-cited safety rules)     │
│  ├── Context builder + curated knowledge pack        │
│  ├── AI response validator                           │
│  └── Follow-up exercise selector                     │
│       ▼              ▼              ▼                │
│     Gemini        Cartesia     Convex mirror         │
│  (expert tutor)   (speech)    (background task)     │
└────────┬──────────────┬──────────────┬───────────────┘
         ▼              ▼              ▼
    Gemini API     Cartesia API   Convex backend
                                       │ reactive queries
                                       ▼
                        ┌────────────────────────────┐
                        │ INSTRUCTOR WEB APP         │
                        │ React + TypeScript         │
                        │ Live port map (top-down)   │
                        │ Safety-event feed          │
                        │ Highlight / pause / "say"  │
                        │ Debrief and progress       │
                        └────────────────────────────┘
```

- **XR:** Unity, OpenXR, Meta XR SDK, passthrough. Desktop preview stays working so backend work can proceed without the headset.
- **Dashboard:** React + TypeScript.
- **Deployment:** one Vultr CPU VM running backend and dashboard behind HTTPS. No GPU needed.

## 1. Evaluation approach

The trainee is evaluated by **deterministic geometry and event rules**, not by a trained model and not by the LLM.

- **Why geometry works:** port placement is a set of measurable quantities: where each port sits relative to landmarks, how far apart the ports are, the insertion angle, and the order of entry. The sim knows these exactly.
- **Why not the LLM as the evaluator:** an LLM grading from a log is not reproducible and is easy to dispute. Gemini never invents a score.
- **Where the research comes in:** guideline and technique sources are encoded as **rules with citations** (section 3). The **expert tutor** (section 4) explains those rule results and answers questions from a curated knowledge pack.
- **No trained models in the MVP.** Port placement starts before any dissection, so the Endoscapes2023 and CholecT50 datasets don't apply.

## 2. Runs inside Unity

Unity owns everything that must react immediately.

**Practice scene:** a virtual abdomen with landmark markers, overlaid on a physical mannequin or tabletop phantom using passthrough. Placements are recorded in the phantom's coordinate frame, so the same numbers work on the desktop preview. Skin and tissue are not simulated. Insufflation is a visual and a timed step, not a physical model.

**Tools:**
- **Entry instrument:** the trainee chooses the entry technique (open Hasson-style or Veress). Each choice has its own rule checks.
- **Trocar and cannula:** placed at a chosen point, with an insertion angle and depth.
- **Camera (laparoscope):** shows the trainee's live view on a virtual monitor. Port placement is judged under camera view wherever the rules require it.
- **Working instruments:** checked for clashes with the other ports.

**Port-placement state machine** (explicit; the LLM never decides what happens next). The order follows standard four-port lap chole practice; verify against the sources in section 3:

```
READY → ENTRY_SITE_SELECTED → ENTRY_PERFORMED → INSUFFLATED → CAMERA_PORT_PLACED
      → WORKING_PORT_1 → WORKING_PORT_2 → WORKING_PORT_3 → LAYOUT_CHECK → REVIEW
        (retry the current step at any point; reset the phantom for a new attempt)
```

**Local geometry check** loads thresholds from `GET /scenarios/port_placement` so Unity and the backend can't drift.

**Tier 0 safety cues** fire locally from a cached WAV and never wait on the backend. Triggers: a trocar aimed at a danger zone (for example the midline above the umbilicus, where the epigastric vessels lie), and a working port placed before the camera is in view.

**Event recorder:** structured actions only, never headset video.

```json
{"event_id":"evt_0042","kind":"port_placed","t_ms":18420,"phase":"WORKING_PORT_2",
 "port":"epigastric_5mm","position_local_mm":[12.4,-38.1,0.0],
 "insertion_angle_deg":46,"target_distance_mm":34,"camera_view":true}
```

Coordinates are in the phantom frame, not the headset's world origin. The dashboard uses the same frame and units.

## 3. Deterministic scoring and rules (backend)

**Port-placement criteria, scored from sim geometry:**

| ID | Criterion | Sim measure |
|---|---|---|
| P1 | Entry site at the umbilicus (standard for the camera port) | distance from landmark ≤ tolerance |
| P2 | Working ports in the correct region (epigastric and both subcostal/flank areas, lateral to the rectus) | each port within its region |
| P3 | Ports spaced so instruments don't clash | minimum pairwise distance ≥ threshold |
| P4 | Working ports triangulate on the gallbladder target | angles between instrument axes and target within range |
| P5 | Insertion angle and depth appropriate for each port | angle and depth within range |
| P6 | Sequence: camera port first; working ports inserted under camera view | event order and `camera_view` flag |

**Safety and technique rules.** Each rule carries a source, and every source must be opened and checked before the demo (same discipline as [COST_DATA.md](COST_DATA.md)):

| Rule ID | Rule | Event check | Source (verify) |
|---|---|---|---|
| `camera_first` | Establish the camera port before placing working ports | working port before camera port | SAGES / EAES guidance on laparoscopic entry and port placement |
| `no_blind_working_port` | Insert working ports under direct camera view | `port_placed` with `camera_view:false` | SAGES / EAES guidance |
| `avoid_midline_danger` | Avoid the midline above the umbilicus where the epigastric vessels lie | trocar within danger zone | Standard anatomy texts; SAGES entry guidance |
| `pneumoperitoneum_before_trocar` | Insufflate before inserting trocars | trocar placed before `INSUFFLATED` | standard practice; check SAGES entry guidance |
| `ports_spaced` | Keep ports apart so instruments don't clash and can triangulate | P3 threshold | ergonomics literature (curated) |
| `ergonomic_angles` | Keep instrument angles workable for the surgeon | P4/P5 ranges | ergonomics literature, e.g. Hanna et al. (verify) |

Rules marked "curated" are standard technique and must say so in the UI. Every numeric threshold in P1–P5 is a **prototype choice**, not a clinical standard, until it has a cited source.

**Metrics:**
- `ports_placed`, per-port `position_error_mm`, `insertion_angle_deg`
- `rule_violations` (IDs + event IDs), `sequence_errors`
- `phase_durations_s`, `elapsed_s`

**`practice_score`:** a documented prototype rubric weighting placement accuracy, sequence and safety, and time. **Capped at 40** after a `avoid_midline_danger` violation or any `camera_first` violation. It is labelled "prototype, not clinically validated, not a certification".

## 4. Expert tutor: measurements first, explanation second

Gemini receives structured evidence plus a **curated knowledge pack**, not an open request to "judge the procedure". The pack lives in `backend/` as versioned JSON. Each entry has an ID, a short explanation, and its source citation. It covers:
- the four-port layout and the purpose of each port
- entry technique and insufflation
- safe trocar placement and danger zones
- why the camera goes first and why ports go in under view

A dozen key sources fit in the prompt, so **no RAG and no Moss** are needed.

Input:
```json
{"scenarioId":"port_placement","scenarioVersion":1,"phase":"WORKING_PORT_2",
 "ports":{"camera":"ok","epigastric":"ok","right_subcostal":"ok"},
 "violations":[{"ruleId":"no_blind_working_port","eventId":"evt_0061"}],
 "knowledge":["kp_camera_first","kp_view_before_working_port"],
 "allowedNextExercises":["port_placement_repeat","port_placement_no_camera_assist"],
 "traineeQuestion":"Why do we place the camera first?"}
```

Output (schema-constrained JSON, Pydantic):
```json
{"summary":"You placed the left working port before the camera was in view.",
 "observations":[{"eventId":"evt_0061","ruleId":"no_blind_working_port","knowledgeId":"kp_view_before_working_port",
   "message":"Working ports should go in under direct camera view so you can see the trocar path and avoid organs."}],
 "nextExerciseId":"port_placement_repeat",
 "spokenFeedback":"Establish the camera first, then place each working port under view.",
 "needsInstructorReview":false}
```

The backend validates every `eventId`, `ruleId`, `knowledgeId` and exercise ID itself. A valid schema does not establish factual correctness.

**Boundaries:**
- The scorer measures, the tutor explains with citations, and the instructor reviews.
- The tutor never changes a placement or a score.
- No clinical validation claims.

## 5. Voice (Cartesia)

| Tier | Trigger | Path | Target (not verified) |
|---|---|---|---|
| 0 Safety | trocar toward danger zone; working port before view | Unity-local rule → cached WAV | < 100 ms |
| 1 Templated cue | step complete, phase change, idle | `GET /cues/{cue_id}/audio`, pre-synthesised and disk-cached | ~150–400 ms |
| 2 Expert tutor | phase end, attempt end, trainee question | `/coach` → Gemini → Cartesia | 2–8 s; text shows first |

- No tutor call ever blocks a safety cue.
- Cue text comes from a versioned, backend-owned template table.
- Provider-specific audio stays inside the speech adapter.
- The MVP uses preset question buttons. Push-to-talk speech-to-text is a later, separately chosen service.
- Required: a text version of every spoken line, a stop-speaking control, and a fallback when speech generation fails.

## 6. Instructor collaboration (nice-to-have)

The instructor sees a synchronized representation, not a headset livestream.

- **Quest → instructor:** events and ~1 Hz state → FastAPI → Convex mirror (background task) → React subscriptions. The dashboard shows a live top-down port map, a step timeline, and a safety-event feed.
- **Instructor → Quest:** command → FastAPI → Quest polls (0.75 s) → Unity applies and acknowledges. Roughly 500–1,000 ms latency is fine.
- **Commands:**
  - `highlight_landmark` (umbilicus, xiphoid, costal margins) or `highlight_port_site`
  - `request_pause`
  - `say`: instructor text spoken on the headset, length-capped server-side
  - assign an implemented exercise
  - request retry

  Expired commands and commands invalid for the current scenario are rejected.
- **Invite:** `POST /sessions/{id}/invite` returns a short join code and an expiring instructor token scoped to that session. The token is checked on commands, annotations and `say`. A join code is an invitation, not a replacement for authorization.

## 7. Patient cost estimate (nice-to-have)

Kept **separate from trainee performance**. A patient never sees a cost derived from a trainee's practice errors.

- **Inputs:**
  - setting: inpatient, outpatient or ASC
  - insurance: deductible remaining, coinsurance and out-of-pocket max, or self-pay
- **Allowed amounts:** public fee schedules for CPT 47562/47563 (surgeon, facility, anesthesia), shown as ranges.
- **Out-of-pocket** = min(deductible + coinsurance × (allowed − deductible), OOP max).
- **Sources:** every figure goes through the source check in [COST_DATA.md](COST_DATA.md). The existing trainee cost module uses OR cost per minute, which is **not** a patient price.
- **Banner on every page:** "Educational illustration only. Not a quote, not medical or financial advice." Each line shows its range, citation and year. Insurance inputs are not stored.

## 8. Data model and API

The backend database (SQLite locally, Postgres when `DATABASE_URL` is set) is the source of truth. Convex is a read-only live mirror.

**New tables:** `pp_events`, `pp_state`. Peg tables are untouched. **Convex adds** `ppState` and `ppEvents`, with optional fields so the existing ingest stays backward compatible.

All endpoints are under `/api/v1`.

| Endpoint | Purpose |
|---|---|
| POST /sessions | Create session; `exercise` ∈ `peg_transfer`, `port_placement` |
| POST /sessions/{id}/pp/events | Upload deduplicated event batch |
| POST /sessions/{id}/pp/state | ~1 Hz placement state snapshot |
| GET /sessions/{id}/pp/status | Deterministic placement status and rule violations |
| GET /scenarios/port_placement | Thresholds, phases, cue table, rule table |
| GET /cues/{cue_id}/audio | Cached cue audio |
| POST /sessions/{id}/complete | Finalize metrics (dispatch by scenario) |
| POST /sessions/{id}/coach | Expert tutor; optional `{question, phase}` |
| GET /sessions/{id}/commands · POST …/ack | Instructor commands |
| POST /sessions/{id}/invite | Nice-to-have: instructor join |
| GET /health | Backend availability |

## 9. Reliability, security and data

| Problem | Required behavior |
|---|---|
| Internet disconnects | Continue local practice; Tier 0 cues still work; buffer events |
| Gemini times out | Show metrics, rule results and deterministic feedback |
| Cartesia fails | Show text feedback |
| Duplicate upload | Dedupe by event ID |
| Convex down | Drop mirror writes; the session is unaffected |
| Command refers to unknown landmark or port | Reject it |

- Keep Gemini, Cartesia and database credentials off the headset and out of browser bundles.
- Enforce session-level authorization; rate-limit tutor requests; use HTTPS.
- The app does not capture or store patient data. It uses a mannequin or phantom only.
- Do not retain raw microphone recordings by default.
- Never execute code returned by AI.

## 10. Milestones

0. **Unblock:** first headset run (see STATUS.md). Add the per-scenario plugin layer in the backend so peg transfer keeps passing its tests. This also fixes the peg-only assumptions in the Convex mirror, `/progress`, `/cost`, assessment, coach and CommandPoller.
1. **Port placement offline:** the rig registers over a phantom, the four ports can be placed and the geometry check works. Tier 0 cues work.
2. **Backend round trip:** events and state upload, deterministic placement status, rule violations and metrics.
3. **Expert tutor:** knowledge pack, cited and validated Gemini feedback, Cartesia playback, cue audio.
4. **Instructor view** (nice-to-have): live port map, highlight / pause / `say`, invite.
5. **Patient cost estimate** (nice-to-have).

**Cut order if short on time:**
1. Patient cost
2. Invite tokens (keep Caddy basic auth)
3. Mid-session phase debriefs (keep end-of-attempt)
4. Insertion-angle scoring (keep position and sequence)

**Never cut:** deterministic placement scoring, the `camera_first` rule, text versions of spoken lines.

## 11. Stretch

- **Dissect to CVS:** the next module, after port placement. It reuses the same rig, event format and tutor, and adds the CVS criteria and clip rules.
- **Trained models:** a quiz on Endoscapes2023 frames (for the CVS module) or a CholecT50 phase model. Both need dataset access and any model ships with a model card: "Research prototype. Not a medical device."

## Open items

- Confirm the video the user referenced is port placement (the title indicates a Spanish-language lap chole demo; the content could not be verified).
- Every rule source in section 3 must be opened and quoted before it is shown to users; until then it is **not verified**.
- Numeric thresholds (P1–P5) and the score cap are prototype choices, not clinically validated.
- Trainee cost rates are sourced in [COST_DATA.md](COST_DATA.md); the per-error adverse-event probability is an explicit assumption.
