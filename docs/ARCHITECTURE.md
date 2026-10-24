# Architecture

**Unity local simulation + one FastAPI backend + deterministic rules + Gemini expert tutor + Cartesia speech + Convex mirror + React instructor dashboard.**
Scenario: **laparoscopic cholecystectomy, focused on reaching the Critical View of Safety (CVS)** before clipping. Peg transfer stays as the existing warm-up scenario. The practice experience runs locally on the headset and never waits on cloud latency. Moss is not used; Cartesia handles voice.

```
┌──────────────────────────────────────────────────────┐
│ META QUEST 3 (UNITY, passthrough)                    │
│  LapCholeRig: stylised liver / gallbladder,          │
│   cystic duct, cystic artery, CBD exclusion zone,    │
│   fat + attachment tiles                             │
│  ├── Tools: grasper, hook (energy), clipper, scissors│
│  ├── Phase state machine                             │
│  ├── Local CVS evaluator + Tier 0 safety cues        │
│  ├── Event recorder (CholecT50-style triplets)       │
│  └── Coach UI + audio playback                       │
│  Local simulation continues without cloud access     │
└───────────────────────┬──────────────────────────────┘
                        │ HTTPS: batched events, ~1 Hz state, questions
                        ▼
┌──────────────────────────────────────────────────────┐
│ FASTAPI BACKEND (VULTR), one application             │
│  Session API · Event ingestion · Command API         │
│              ▼                                       │
│  Scenario plugins (peg_transfer, lap_chole)          │
│  ├── Deterministic CVS scorer                        │
│  ├── Rule engine (literature-cited safety rules)     │
│  ├── Context builder + curated knowledge pack        │
│  ├── AI response validator                           │
│  └── Follow-up exercise selector                     │
│       ▼              ▼              ▼                │
│     Gemini        Cartesia     Convex mirror         │
│  (expert tutor)   (speech)    (background task)      │
└────────┬──────────────┬──────────────┬───────────────┘
         ▼              ▼              ▼
    Gemini API     Cartesia API   Convex backend
                                       │ reactive queries
                                       ▼
                        ┌────────────────────────────┐
                        │ INSTRUCTOR WEB APP         │
                        │ React + TypeScript         │
                        │ Live CVS bars, phase line  │
                        │ Safety-event feed          │
                        │ Highlight / pause / "say"  │
                        │ Debrief and progress       │
                        └────────────────────────────┘
```

- **XR:** Unity, OpenXR, Meta XR SDK, passthrough. Desktop preview stays working so backend work can proceed without the headset.
- **Dashboard:** React + TypeScript.
- **Deployment:** one Vultr CPU VM running backend and dashboard behind HTTPS. No GPU needed for the MVP.

## 1. Evaluation approach

The trainee is evaluated by **deterministic rules over sim state and events**, not by a trained model and not by the LLM.

- **Why not a trained classifier as the evaluator:** Endoscapes2023 is real laparoscope video; the sim renders stylised anatomy. A CVS classifier trained on Endoscapes would be out of distribution on sim frames, so its output can't back a score. The sim already knows the ground-truth geometry, so it can score CVS exactly.
- **Why not the LLM as the evaluator:** an LLM grading from a log is not reproducible and is easy to dispute. Gemini never invents a score.
- **Where the research comes in:** surgical-safety guidance from the literature is encoded as **rules with citations** (section 3). The **expert tutor** (section 4) explains those rule results and answers questions from a curated knowledge pack.

Model training (CVS classifier on Endoscapes2023, phase model on CholecT50) is **out of the MVP**. It's a stretch goal (section 11).

## 2. Runs inside Unity

Unity owns everything that must react immediately.

**Practice scene:** stylised anatomy built from primitives. A liver slab, a gallbladder capsule, cystic duct and cystic artery cylinders, and the common bile duct (CBD) as an exclusion zone. Removable tiles represent:
- fat in the hepatocystic triangle (CVS criterion 2)
- tissue around the duct and artery (criterion 1)
- lower-third gallbladder attachments to the liver bed (criterion 3)

Tissue deformation and realistic cutting are **not** prerequisites for the demo.

**Tools:**
- **Grasper:** pinch holds the infundibulum and sets a "retracted" flag that opens the triangle.
- **Hook:** pinch near a tile removes it. Near the CBD it emits an `injury` event.
- **Clipper:** three clip targets with tolerance.
- **Scissors.**

**Phase state machine** (explicit; the LLM never decides what happens next). Phase names follow Cholec80/CholecT50:

```
READY → PREPARATION → CALOT_TRIANGLE_DISSECTION → [VERIFY_CVS] → CLIPPING_CUTTING
      → GALLBLADDER_DISSECTION → COMPLETE → REVIEW
        (pause / retry available in any phase)
```

`VERIFY_CVS` is an explicit "time-out" step the trainee triggers before clipping.

**Local CVS evaluator** loads thresholds from `GET /scenarios/lap_chole_cvs` so Unity and the backend can't drift.

**Tier 0 safety cues** fire locally from a cached WAV and never wait on the backend. Triggers: clip about to be applied before CVS; energy near the CBD.

**Event recorder:** structured actions, never headset video. Events use the CholecT50 vocabulary: instrument, verb, target.

```json
{"event_id":"evt_0042","kind":"triplet","t_ms":18420,"phase":"calot_triangle_dissection",
 "instrument":"hook","verb":"dissect","target":"cystic_duct"}
```

Coordinates are relative to the practice model, not the headset's world origin. The dashboard uses the same model and units.

## 3. Deterministic scoring and literature rules (backend)

**CVS criteria, scored from sim state:**

| ID | Criterion | Sim measure |
|---|---|---|
| C1 | Two and only two structures entering the gallbladder | duct and artery exposure, each ≥ threshold |
| C2 | Hepatocystic triangle cleared of fat and fibrous tissue | removed triangle tiles / total ≥ threshold |
| C3 | Lower third of gallbladder separated from the liver bed | removed lower-third tiles / total ≥ threshold |

**Safety and technique rules.** Each rule carries a source, and every source must be opened and checked before the demo (same discipline as [COST_DATA.md](COST_DATA.md)):

| Rule ID | Rule | Event check | Source (verify) |
|---|---|---|---|
| `cvs_before_clip` | No clipping or cutting until C1–C3 are all met | `clip_applied`/`cut` while any criterion false | Strasberg & Brunt, J Am Coll Surg 2010; SAGES Safe Cholecystectomy |
| `timeout_before_clip` | Pause and verify CVS before clipping | no `VERIFY_CVS` before first clip | SAGES Safe Cholecystectomy (time-out) |
| `cbd_danger_zone` | Avoid energy near the CBD; dissect high on the gallbladder | `injury:thermal_cbd`, energy within CBD margin | SAGES Safe Cholecystectomy; Rouvière's sulcus literature |
| `retraction_opens_triangle` | Retract the infundibulum laterally before dissecting the triangle | `dissect` in triangle without `retracted` flag | technique texts (curated) |
| `clip_placement` | Two clips proximal, one distal on the duct; artery clipped before division | clip targets and order | curated |
| `bailout_when_stuck` | If CVS isn't reached in time, stop and consider help or bailout | phase time > limit without CVS | Tokyo Guidelines 2018; SAGES ("get help") |

Rules marked "curated" are drawn from standard technique and must say so in the UI.

**Metrics:**
- `criteria_met_at_ms` (one per criterion), `cvs_achieved`, `cvs_time_s`
- `rule_violations` (IDs + event IDs), `injury_count` by type, `clips_correct`
- `phase_durations_s`, `elapsed_s`

**`practice_score`:** a documented prototype rubric weighting completion, safety and time, **capped at 40** after a `cvs_before_clip` violation or any CBD injury. It is labelled "prototype, not clinically validated, not a certification".

## 4. Expert tutor: measurements first, explanation second

Gemini receives structured evidence plus a **curated knowledge pack**, not an open request to "judge the surgery". The pack lives in `backend/` as versioned JSON. Each entry has an ID, a short explanation, and its source citation. It covers:
- the CVS definition and why each criterion matters
- the SAGES safe-chole strategies
- bailout options
- the phase definitions

A dozen key sources fit in the prompt, so **no RAG and no Moss** are needed. Add retrieval only if the corpus grows large.

Input:
```json
{"scenarioId":"lap_chole_cvs","scenarioVersion":1,"phase":"clipping_cutting",
 "cvs":{"c1":true,"c2":true,"c3":false},
 "violations":[{"ruleId":"cvs_before_clip","eventId":"evt_0107"}],
 "knowledge":["kp_cvs_c3","kp_sages_timeout"],
 "allowedNextExercises":["cvs_lower_third_focus","dissect_to_cvs_repeat"],
 "traineeQuestion":"Why does the lower third matter?"}
```

Output (schema-constrained JSON, Pydantic):
```json
{"summary":"You clipped before the lower third was off the liver bed.",
 "observations":[{"eventId":"evt_0107","ruleId":"cvs_before_clip","knowledgeId":"kp_cvs_c3",
   "message":"CVS requires the lower third separated so both structures are seen entering the gallbladder."}],
 "nextExerciseId":"cvs_lower_third_focus",
 "spokenFeedback":"Clear the lower third before you clip. Then call your time-out.",
 "needsInstructorReview":false}
```

The backend validates every `eventId`, `ruleId`, `knowledgeId` and exercise ID itself. A valid schema does not establish factual correctness.

**Boundaries:**
- The scorer measures, the tutor explains with citations, and the instructor reviews.
- The tutor never changes a criterion state or a score.
- No clinical validation claims.

## 5. Voice (Cartesia)

| Tier | Trigger | Path | Target (not verified) |
|---|---|---|---|
| 0 Safety | clip before CVS; energy near CBD | Unity-local rule → cached WAV | < 100 ms |
| 1 Templated cue | criterion met, phase change, idle | `GET /cues/{cue_id}/audio`, pre-synthesised and disk-cached | ~150–400 ms |
| 2 Expert tutor | phase end, attempt end, trainee question | `/coach` → Gemini → Cartesia | 2–8 s; text shows first |

- No tutor call ever blocks a safety cue.
- Cue text comes from a versioned, backend-owned template table.
- Provider-specific audio stays inside the speech adapter.
- The MVP uses preset question buttons. Push-to-talk speech-to-text is a later, separately chosen service.
- Required: a text version of every spoken line, a stop-speaking control, and a fallback when speech generation fails.

## 6. Instructor collaboration (nice-to-have)

The instructor sees a synchronized representation, not a headset livestream.

- **Quest → instructor:** events and ~1 Hz CVS state → FastAPI → Convex mirror (background task) → React subscriptions. The dashboard shows live CVS bars, a phase timeline and a safety-event feed.
- **Instructor → Quest:** command → FastAPI → Quest polls (0.75 s) → Unity applies and acknowledges. Roughly 500–1,000 ms latency is fine.
- **Commands:**
  - `highlight_structure`, with targets `cystic_duct`, `cystic_artery`, `cbd`, `triangle`, `lower_third`
  - `request_pause` ("verify CVS")
  - `say`: instructor text spoken on the headset, length-capped server-side
  - assign an implemented exercise
  - request retry

  Expired commands and commands invalid for the current scenario are rejected.
- **Invite:** `POST /sessions/{id}/invite` returns a short join code and an expiring instructor token scoped to that session. The token is checked on commands, annotations and `say`. A join code is an invitation, not a replacement for authorization.

## 7. Patient cost estimate (nice-to-have)

Kept **separate from trainee performance**. A patient never sees a cost derived from a trainee's sim errors.

- **Inputs:**
  - setting: inpatient, outpatient or ASC
  - insurance: deductible remaining, coinsurance and out-of-pocket max, or self-pay
- **Allowed amounts:** public fee schedules for CPT 47562/47563 (surgeon, facility, anesthesia), shown as ranges.
- **Out-of-pocket** = min(deductible + coinsurance × (allowed − deductible), OOP max).
- **Sources:** every figure goes through the source check in [COST_DATA.md](COST_DATA.md). The existing trainee cost module uses OR cost per minute, which is **not** a patient price.
- **Banner on every page:** "Educational illustration only. Not a quote, not medical or financial advice." Each line shows its range, citation and year. Insurance inputs are not stored.

## 8. Data model and API

The backend database (SQLite locally, Postgres when `DATABASE_URL` is set) is the source of truth. Convex is a read-only live mirror.

**New tables:** `lc_events`, `lc_state`. Peg tables are untouched. **Convex adds** `lcState` and `lcEvents`, with optional fields so the existing ingest stays backward compatible.

All endpoints are under `/api/v1`.

| Endpoint | Purpose |
|---|---|
| POST /sessions | Create session; `exercise` ∈ `peg_transfer`, `lap_chole_cvs` |
| POST /sessions/{id}/lc/events | Upload deduplicated event batch |
| POST /sessions/{id}/lc/state | ~1 Hz CVS state snapshot |
| GET /sessions/{id}/lc/cvs | Deterministic CVS status and rule violations |
| GET /scenarios/lap_chole_cvs | Thresholds, phases, cue table, rule table |
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
| Command or annotation refers to unknown target | Reject it |

- Keep Gemini, Cartesia and database credentials off the headset and out of browser bundles.
- Enforce session-level authorization; rate-limit tutor requests; use HTTPS.
- The app never captures or stores patient data. If public research datasets are used (Endoscapes2023, CholecT50), they are de-identified, used under their licence (CC BY-NC-SA 4.0, non-commercial), attributed in the UI, and kept out of git and the APK.
- Do not retain raw microphone recordings by default.
- Never execute code returned by AI.

## 10. Milestones

0. **Unblock:** first headset run (see STATUS.md). Add the per-scenario plugin layer in the backend so peg transfer keeps passing its tests. This also fixes the peg-only assumptions in the Convex mirror, `/progress`, `/cost`, assessment, coach and CommandPoller.
1. **Dissect to CVS offline:** the lap-chole rig installs, tiles can be removed, the local CVS evaluator and Tier 0 cues work.
2. **Backend round trip:** events and state upload, deterministic CVS status, rule violations and metrics.
3. **Expert tutor:** knowledge pack, cited and validated Gemini feedback, Cartesia playback, cue audio.
4. **Instructor view** (nice-to-have): live CVS dashboard, highlight / pause / `say`, invite.
5. **Patient cost estimate** (nice-to-have).

**Cut order if short on time:**
1. Patient cost
2. Invite tokens (keep Caddy basic auth)
3. Mid-session phase debriefs (keep end-of-attempt)
4. C3 in the sim

**Never cut:** deterministic CVS scoring, the `cvs_before_clip` rule, text versions of spoken lines.

## 11. Stretch: trained models

Only after the MVP is working:

- **"Spot the CVS" quiz:** a virtual monitor shows held-out Endoscapes2023 test frames. The trainee calls each criterion and is scored against the expert labels. No training needed, but it does need the dataset download.
- **CVS classifier:** a pretrained CNN fine-tuned on Endoscapes2023 with a video-level split, exported to ONNX and run on CPU. Shown only as an "AI second opinion" in the quiz, with predictions precomputed. It never scores the sim.
- **CholecT50 priors:** per-phase triplet frequencies and durations feed the tutor ("expected actions in this phase"). Access requires an agreement form.

Any model ships with a model card: "Research prototype. Not a medical device. Metrics on the dataset test split only."

## Open items

- Rule sources in section 3 must be opened and quoted before they are shown to users; until then they are **not verified**.
- CVS thresholds and the score cap are prototype choices, not clinically validated.
- Trainee cost rates are sourced in [COST_DATA.md](COST_DATA.md); the per-error adverse-event probability is an explicit assumption.
