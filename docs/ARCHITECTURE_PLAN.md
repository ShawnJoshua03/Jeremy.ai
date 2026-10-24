# Jeremy.ai v2 plan: laparoscopic cholecystectomy (CVS-focused) in AR

Status: plan only. No code written. Dataset facts come from memory of the papers and must be re-checked after download. All latency figures are estimates and are **not verified**.

Sources read: AGENTS.md, docs/HANDOFF.md, docs/ARCHITECTURE.md, docs/COST_DATA.md, docs/STATUS.md, backend (main, scoring, assessment, coach, cost, convex_mirror, speech/, mentorship/), Unity rig, state machine, ApiClient, CoachAudioPlayer, CommandPoller, dashboard Convex schema and http.ts, deploy/.

---

## 0. Fix before any new code

1. **Nothing has run on the headset yet** (STATUS.md:30, :54). Do headset task #1 first. If passthrough, hands, or LAN HTTP are broken, the scenario doesn't matter.
2. **Rule conflict.** ARCHITECTURE.md:199 says "Use synthetic practice cases, never patient data." Endoscapes and CholecT50 are de-identified patient video. Reword to: "de-identified public research datasets under their licence, used for non-commercial purposes; never user or patient data captured by the app." Apply in ARCHITECTURE.md and AGENTS.md.
3. **Peg-transfer assumptions break a second scenario, some silently:**
   - `convex_mirror._session` reads peg metric keys (convex_mirror.py:57-69) inside a bare try/except (:164-167). A lap-chole session would silently never mirror.
   - `/progress` and `/cost` read `drops`, `invalid_releases`, `path_efficiency` (main.py:505-506, :527-529). They would raise KeyError (HTTP 500).
   - `assessment.values` assumes peg keys (assessment.py:76-84).
   - `coach._prompt` hardcodes "peg-transfer" (coach.py:151); fallback text hardcodes "of 6 transfers" (coach.py:141).
   - Convex `sessionMetrics` validator and event `kind` union are peg-only (dashboard/convex/schema.ts:11-21, :41-45).
   - Unity CommandPoller rejects any exercise other than peg (CommandPoller.cs:111); highlights only work via `PegBoard.TryTargetIndex` (:99).
   - `SessionCreate.exercise` accepts only `peg_transfer` (main.py:151).

   **Fix: add a per-scenario plugin layer first.** Peg transfer keeps passing its 145 tests unchanged.

---

## 1. Core design: what the CVS classifier classifies

### Domain gap
Endoscapes2023 is real laparoscope footage (specular tissue, smoke, blood, 30° scope view). The Unity sim renders stylised synthetic anatomy from the trainee's viewpoint. A CNN fine-tuned on Endoscapes and run on sim frames produces numbers that look meaningful but aren't, and errors would leak into scoring.

### Options
| Option | Pros | Cons |
|---|---|---|
| A. Run classifier on sim frames | One pipeline | Severe domain gap; output unvalidated; score not defensible; needs render capture and upload |
| B. Domain adaptation / style transfer | Addresses gap | Not feasible in hackathon; still unvalidated |
| C. **Two tracks:** deterministic scoring from sim state; model on real Endoscapes frames in AR | Scoring stays deterministic; model only sees in-distribution data | Two modes to build |

### Recommendation: Option C

**Track A: hands-on sim ("Dissect to CVS").** Sim knows ground-truth geometry; backend scores each criterion from state, no ML:
- **C1, two structures:** cystic duct and cystic artery exposure, each ≥ threshold.
- **C2, hepatocystic triangle cleared:** removed triangle fat tiles / total ≥ threshold.
- **C3, lower third off liver bed:** removed lower-third attachment tiles / total ≥ threshold.
- Teachable safety rule: **`clip_before_cvs`** = clip applied while any criterion is false.

**Track B: recognition drill ("Call the CVS").** A virtual monitor in passthrough steps through held-out **Endoscapes test-split** frame sequences. Trainee says yes/no per criterion or "CVS achieved".
- Scoring is deterministic against expert consensus labels, not against the model.
- Classifier is the AI second opinion: per-frame P(C1/C2/C3) trace between sparse labelled frames; tutor explains divergences.
- Predictions precomputed offline; no runtime inference.

**Optional, experimental: `POST /cvs/infer` on sim captures.** Shown only as a "domain-gap demo" beside sim truth. Never feeds a score. Cut first.

### CholecT50 (phases and triplets)
- **Sim emits its own ground truth.** Each event logged in CholecT50 vocabulary: instrument (grasper, hook, clipper, scissors), verb (retract, dissect, clip, cut), target (gallbladder, cystic_duct, cystic_artery, cystic_plate, liver). Phase from the sim state machine: `preparation`, `calot_triangle_dissection`, `clipping_cutting`, `gallbladder_dissection`.
- **CholecT50 used offline as priors, not a runtime model.** Script builds `lapchole_priors.json`: per phase, top-k triplet frequencies and median/IQR phase duration. Tutor context gets "expected actions in this phase" beside "observed triplets". Flags like "clipper used during calot_triangle_dissection" are rule-based, not Gemini.
- Phase/triplet recognition model is out of scope. Phase-only ResNet on CholecT50 frames is a stretch goal.
- **Fallback** if access is slow: hand-curated phase/action table from published phase definitions, labelled "curated, not data-derived."

---

## 2. System architecture

```
QUEST 3 (Unity, passthrough)
 ├─ Scenario A: LapCholeRig – stylised liver/GB, duct/artery/CBD, fat + attachment tiles,
 │    grasper(retract) / hook(dissect, pinch=energy) / clipper / scissors,
 │    LapCholeStateMachine (phases), local CvsEvaluator (same thresholds as backend),
 │    local safety cues (Tier 0)
 ├─ Scenario B: ReferenceMonitor – frame sequence player + yes/no criterion buttons
 └─ existing: UploadQueue, ApiClient, CoachPanel, CoachAudioPlayer, CommandPoller
        │ HTTPS (or LAN http in testing)
        ▼
FASTAPI (Vultr CPU VM, single uvicorn, deploy/)
 ├─ scenarios/{peg_transfer,lap_chole}.py – metrics, assess, drills, coach context, fallback
 ├─ lapchole/router.py – lc events, state snapshots, CVS status, cues, recognition calls
 ├─ cvs_model/ – onnxruntime CPU session (lazy-loaded), precomputed timelines
 ├─ coach.py (Gemini, scenario-aware) → speech/ (Cartesia) + cue-audio disk cache
 ├─ mentorship/ (commands + new types); convex_mirror (moved to BackgroundTasks)
 └─ cost.py (+ optional patient estimator module)
        │ mirror (≤1 Hz state)              ▲ poll commands (0.75 s, CommandPoller.cs:34)
        ▼                                    │
CONVEX (read-only mirror) ──reactive──► React dashboard (live CVS bars, phase timeline,
                                         safety events, highlight/pause/"say" buttons)
OFFLINE (GPU laptop / Colab, one-off): train CVS → ONNX → precompute timelines;
                                         CholecT50 → priors JSON
```

### Inference
- **Training:** once, on rented or free GPU.
- **Runtime:** ONNX Runtime CPU on the existing Vultr VM. No GPU VM needed.
- ResNet-18 or EfficientNet-B0 at 224 px on CPU: roughly 20–60 ms/frame (**not verified**).

### Voice: Cartesia, not Moss
- Cartesia is integrated and verified with a live call from a PC (STATUS.md:17; `backend/speech/cartesia.py`). Headset playback still unverified (STATUS.md:54).
- Moss is a retrieval runtime, not TTS. HANDOFF.md:15 dropped it; no RAG needed.
- Speech-to-text, if added, is a separate explicit choice (ARCHITECTURE.md:136). MVP uses preset question buttons.

### Voice latency tiers
| Tier | Trigger | Path | Budget (estimate) |
|---|---|---|---|
| 0 Safety | about to clip before CVS; energy near CBD | Unity-local rule → cached WAV on headset | < 100 ms |
| 1 Templated cue | criterion achieved, phase change, idle | Unity → `GET /cues/{cue_id}/audio` (pre-synthesised, disk-cached) | ~150–400 ms |
| 2 Gemini tutor | phase end, attempt end, trainee question | `/coach` (12 s cap, coach.py:25) → `/coach/audio` | 2–8 s; text shows first |

**Rule:** no Gemini call ever blocks a safety cue. Cue text comes from a backend-owned, versioned template table. Every spoken line has a text twin (ARCHITECTURE.md:138).

---

## 3. API and data contracts (all under `/api/v1`)

### Changes to existing endpoints
- `POST /sessions`: `exercise` becomes `Literal["peg_transfer","lap_chole_cvs","cvs_recognition"]` (main.py:151).
- `POST /sessions/{sid}/complete` dispatches to `scenarios[row.exercise].compute()` (replaces peg-only `_compute`, main.py:272-284).
- `POST /sessions/{sid}/coach` accepts optional body `{question?, phase?}`. Today it 409s unless completed (main.py:535-536). For lap chole, also allow a running session when `phase` is completed (phase-end debrief).
- `/cost`, `/progress`, `/assessment` branch by scenario. Lap-chole `/cost` returns 409 until its cost mapping is defined.
- `mentorship/router.py` `COMMAND_TYPES` (:23-29) gains `highlight_structure` (targetId ∈ cystic_duct, cystic_artery, cbd, triangle, lower_third) and `say` (instructor text spoken via `/coach/audio`). Mirror in `_COMMAND_TYPES` (convex_mirror.py:19-27) and Convex `commandType` (schema.ts:4-9).

### New endpoints (`backend/lapchole/router.py`)
| Endpoint | Body / response |
|---|---|
| `POST /sessions/{sid}/lc/events` | `{events:[{event_id, kind, t_ms, phase, instrument?, verb?, target?, value?}]}`. `kind` ∈ triplet, phase_start, phase_end, criterion_met, criterion_lost, clip_applied, cut, injury (thermal_cbd, thermal_duct, tear), clip_before_cvs. Dedupe on (session_id, event_id) via `_insert_new` (main.py:256) |
| `POST /sessions/{sid}/lc/state` | ~1 Hz `{t_ms, phase, c1:{duct, artery}, c2, c3}`. Last-write-wins; mirrored in background |
| `GET /sessions/{sid}/lc/cvs` | `{criteria:[{id, value, threshold, met, met_at_ms}], cvs_achieved, cvs_achieved_at_ms, clip_before_cvs, thresholds_version}`. Deterministic |
| `GET /scenarios/lap_chole_cvs` | Thresholds, phases, tile counts, cue table `{cue_id, text, priority}`. Unity loads thresholds from here so they can't drift |
| `GET /cues/{cue_id}/audio` | WAV, synthesised once per (cue_id, voice), disk-cached. 503 = text only |
| `GET /reference/clips` | `[{clip_id, n_frames, fps, source, license, attribution}]` |
| `GET /reference/clips/{id}/frames/{i}.jpg` | Static frame from a data dir outside the repo |
| `GET /reference/clips/{id}/timeline` | `[{i, gt:{c1,c2,c3}|null, pred:{c1,c2,c3}, model_version}]` |
| `POST /sessions/{sid}/recognition/calls` | `{call_id, clip_id, frame_i, claimed:{c1,c2,c3}}` → `{correct, gt, pred}`. Scored against nearest labelled frame only |
| `POST /cvs/infer` (experimental) | JPEG ≤ 512 KB → `{c1,c2,c3, model_version, latency_ms, domain:"sim", disclaimer}`. Rate-limited via existing `ratelimit` |

**Frame fetches from Unity** use `UnityWebRequest` so `ApiClient.ApplyAuth` (ApiClient.cs:45-49) adds the API key. VideoPlayer URLs can't carry the header.

### Lap-chole metrics (prototype rubric, documented in module docstring like scoring.py:1-11)
`criteria_met_at_ms` ×3, `cvs_achieved`, `cvs_time_s`, `clip_before_cvs`, `injury_count` by type, `clips_correct` (2 proximal, 1 distal on duct, artery clips within tolerance), `phase_durations_s`, `off_prior_triplets`, `elapsed_s`.

`practice_score`: weighted completion, safety, and time (as scoring.py:99). **Capped at 40 if `clip_before_cvs` or any CBD injury.** Labelled "prototype, not clinically validated."

### Storage
New tables `lc_events`, `lc_state`, `recognition_calls` in `database.py` using the `init_db` pattern. Peg tables untouched.

**Convex additions:** tables `lcState`, `lcEvents`; optional `lcMetrics` on sessions; optional arrays in the mirror body (keep http.ts:59-71 backward compatible). Move `convex_mirror` to FastAPI `BackgroundTasks` so the 3 s synchronous post (convex_mirror.py:143-153) stays off the 1 Hz path.

### Reuse vs new
| Reuse as-is | Generalise | New |
|---|---|---|
| ApiClient, UploadQueue, CoachAudioPlayer, ExerciseStateMachine, ratelimit, cartesia.py, command poll/ack, annotations, dedupe, Convex ingest auth | coach.py, assessment (per-scenario thresholds), CommandPoller (`ITargetSurface`), dashboard session list and replay, mirror payload | LapCholeRig, tile anatomy and tools, phase machine, CvsEvaluator, ReferenceMonitor, scenario plugin, lapchole router, cvs_model, priors, Convex lc tables, live CVS panel |

---

## 4. ML plan

### Data (verify on download)
- **Endoscapes2023** (CAMMA): 201 lap chole videos; CVS subset has dissection-phase frames labelled by three experts. Use the official video-level train/val/test split. Licence CC BY-NC-SA 4.0 (non-commercial, attribution, share-alike). Show attribution in app and dashboard.
- **CholecT50** (CAMMA): 50 videos; triplet labels (6 instruments, 10 verbs, 15 targets, 100 triplets) and phases. Access requires an agreement form: **file the request on day 0.** Licence CC BY-NC-SA 4.0. Derived priors need attribution and share-alike note.
- **Check for video overlap** between datasets before claiming any cross-dataset result.
- **Keep raw data out of git and the APK.** Store on backend data volume.

### CVS model
- Backbone: ImageNet-pretrained ResNet-50 or ConvNeXt-Tiny (EfficientNet-B0 if CPU latency matters), 224–320 px, 3-logit multi-label head.
- Training: BCE with per-criterion `pos_weight` (C1 and C3 are rare). Labels = mean of annotators, binarised at 0.5 (or soft-label BCE). Augment with colour jitter, random resized crop; ablate horizontal flip (laterality). 10–15 epochs, early stopping on validation mAP.
- Splits by video only, per official split. Never by frame.
- Test metrics: per-criterion AP and mAP; balanced accuracy at validation-tuned thresholds; AP for "CVS achieved"; calibration plot if time. Published image-only baselines are modest; expect similar and report only computed numbers.
- Export: ONNX (opset 17) plus `model_card.md`. Record SHA-256 and a `model_version` returned in every response. TorchScript only as fallback.
- Precompute: 8–12 test-split sequences of ~30–90 frames crossing CVS achievement → timeline JSON.
- Optional domain-gap comparison on ~50 sim captures; report honestly (likely low agreement).
- **Effort:** ~1 person-day total; download size is the wildcard.

### CholecT50 priors
`build_cholect50_priors.py` → `lapchole_priors.json` (per-phase triplet top-k, instrument usage, duration median/IQR). ~2–3 h once access granted. No model.

**Code location:** new top-level `ml/` for training, export, priors scripts (grok adds owner row to AGENTS.md). Runtime loader `backend/cvs_model/` is claude's.

---

## 5. Work breakdown (MVP first)

### M0: unblock (day 0, parallel)
- user: request CholecT50 access; start Endoscapes download; run headset task #1 (STATUS.md:30).
- grok (docs): ADR-002 "deterministic sim CVS + model on real reference frames"; reword patient-data rule; dataset attribution and licence section; add `ml/` owner row.

### M1: backend scenario layer (claude)
1. `scenarios/` plugin: move peg logic in unchanged; dispatch `_compute`, `/cost`, `/progress`, `/assessment`, coach by `row.exercise`. Fix silent mirror KeyError.
2. `lap_chole` scenario: `cvs.py` scorer, metrics, rubric, assessment thresholds, drills ("Dissect to CVS", "CVS: lower third focus", "Clip placement", "Call the CVS"), Gemini context (CVS state, phase, observed vs prior triplets, safety events), fallback.
3. `lapchole/router.py`: events, state, cvs, scenario config, cue table, cue-audio cache.
4. Convex mirror to background tasks; `lcState`/`lcEvents` payloads.

### M2: Unity "Dissect to CVS" (codex)
1. `LapCholeRig`, modelled on PegTransferRig.cs:33-74. Primitive anatomy (liver slab, GB capsule, duct/artery cylinders, CBD exclusion zone); fat tile grid over triangle (C2); tiles around structures (C1); lower-third attachment tiles (C3).
2. Tools: grasper (pinch holds infundibulum, sets retracted flag); hook (pinch near tile removes it; near CBD emits `injury`); clipper (3 targets with tolerance); scissors.
3. `LapCholeStateMachine` on top of `ExerciseStateMachine`; `CvsEvaluator` loads thresholds from `/scenarios/...`.
4. Tier 0 local safety: fires `clip_before_cvs`, plays cached WAV.
5. Events and 1 Hz state via `UploadQueue`. CommandPoller generalised to `ITargetSurface`; handles `highlight_structure` and `say`.
6. Keep DesktopPreview working so backend work proceeds without the headset.

### M3: ML (owner of `ml/`; claude for runtime)
Train, evaluate, export ONNX, precompute timelines, build priors JSON; add `backend/cvs_model/` loader and `/reference/*` endpoints.

### M4: Unity recognition drill (codex)
`ReferenceMonitor`: world-space quad, frame stepper (play/pause/scrub), three yes/no toggles, "CVS achieved" call. After each call, shows expert labels and model trace as sparklines.

### M5: tutor and voice (claude: coach; opencode/grok: speech)
- Phase-end and end-of-attempt Gemini debriefs citing only event and criterion IDs; extend `coach.validate` (coach.py:170-189).
- Cue template table; cue-audio pre-synthesis at startup.
- Server-side length cap on `say` text.

### M6: dashboard live view (copilot)
- Convex `lcState`/`lcEvents` subscriptions: three criterion bars, phase timeline, safety-event feed.
- Buttons: highlight structure, pause ("verify CVS"), "say".
- Session list handles metrics union. Dataset attribution footer.

### Tests (grok, `backend/tests/`)
- CVS threshold boundaries; `clip_before_cvs`; injury caps on score
- lc event dedupe; mirror payload for lap-chole (regression for silent failure)
- coach validation rejects unknown criterion/event IDs
- `/cvs/infer` with stub model (CI without ONNX)
- timeline schema; recognition scoring against GT only
- peg tests stay green

### Nice-to-have 1: instructor live invite (copilot + claude)
- Today anyone who can reach the API can post commands (STATUS.md:46-47).
- `POST /sessions/{sid}/invite` returns short join code and expiring HMAC instructor token (~2 h, scoped to sid).
- Dashboard opens `?join=CODE`; token checked on `commands`, `annotations`, `say`. Convex queries filtered by sid.
- Quest shows code on HUD. Planned join endpoint: ARCHITECTURE.md:85, :173. **Effort ~0.5 day.**

### Nice-to-have 2: patient cost estimator (claude + grok for sources)
- Keep separate from trainee performance. Never show a patient a cost derived from trainee sim errors.
- Existing rates (COST_DATA.md:9-20) are hospital OR cost per minute and 1994 instrument costs. **Not patient prices.**
- Model: inputs = setting (inpatient / outpatient / ASC) and insurance (deductible remaining, coinsurance, OOP max) or self-pay. Allowed amounts from public schedules for CPT 47562/47563 (Medicare PFS surgeon fee, OPPS/ASC facility, anesthesia base units), as ranges.
- Out-of-pocket = min(deductible + coinsurance × (allowed − deductible), OOP max).
- Every figure passes the source check in COST_DATA.md:40-58. **Effort ~0.5–1 day, mostly sourcing.**

---

## 6. Risks and cut-lines

| Risk | Mitigation |
|---|---|
| Headset path unproven | M0 headset run first; desktop preview for dev |
| CholecT50 access delayed | Curated phase table, labelled "curated" |
| Endoscapes download/training slow | Track B works with GT only; model trace hidden |
| Tile anatomy too fiddly | Reduce to C2 tiles + clip placement; C1/C3 from fixed tile groups |
| Sim-frame model gives nonsense | Experimental, never scored; cut first |
| Gemini latency | Tier 0/1 don't depend on it; 12 s budget and fallback exist (coach.py:25, :197-214) |
| 1 Hz mirror adds latency | Background tasks; throttle; drop state if Convex down |

**Cut order (first to drop)**
1. Patient cost estimator
2. `/cvs/infer` on sim frames
3. Instructor invite tokens (keep Caddy basic auth, deploy/README.md:36-41)
4. Mid-session Gemini phase debriefs (keep end-of-attempt)
5. Model trace in Track B (keep GT-scored drill)
6. CholecT50 priors (use curated table)
7. C3 in the sim

**Never cut:** deterministic CVS scoring, `clip_before_cvs` safety rule, text twins for voice.

---

## 7. Required labels (AGENTS.md rules)

- **"Not verified" until run:** every new integration, including headset cue-audio playback, lap-chole live mirror, onnxruntime on Vultr, Quest frame fetch, latency tiers, and Cartesia in-headset playback.
- **CVS model:** "Research prototype trained on Endoscapes2023 (CC BY-NC-SA 4.0). Metrics on Endoscapes test split only. Not a medical device; not for clinical decisions." Show `model_version` everywhere. Sim-frame inference: "Experimental: out of distribution."
- **Sim CVS thresholds and lap-chole score:** "Prototype rubric, not clinically validated, not a certification."
- **Gemini:** explains measured values only; criterion/event IDs validated server-side; never changes a criterion state or score.
- **Trainee cost (existing):** "Illustrative estimate, not billing data"; per-error risk is an assumption (cost.py:20; COST_DATA.md:28).
- **Patient cost:** persistent banner: "Educational illustration only. Not a quote, not medical or financial advice. Your actual cost depends on your insurer, facility, surgeon, anesthesia, pathology, and complications. Contact your insurer and hospital (US hospitals publish price-transparency files)." Show range, citations, and year on every line. Do not store user insurance inputs.
- **Datasets:** attribution and licence in dashboard footer and Track B monitor; non-commercial use only.
