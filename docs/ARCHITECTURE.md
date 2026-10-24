# Architecture

**Unity local simulation + one FastAPI backend + Gemini coaching + Cartesia speech + Convex collaboration + React instructor dashboard.**
The practice experience runs locally on the headset and never waits on cloud latency. Moss is not used; Cartesia handles voice.

```
┌──────────────────────────────────────────────────────┐
│ META QUEST 3 (UNITY)                                 │
│  Surgical practice scene                             │
│  ├── Tool grabbing and controller interaction        │
│  ├── Local collision detection                       │
│  ├── Task / procedure state machine                  │
│  ├── Immediate visual feedback                       │
│  ├── Event recorder and local metrics                │
│  └── Coach UI + audio playback                       │
│  Local simulation continues without cloud access     │
└───────────────────────┬──────────────────────────────┘
                        │ HTTPS: batched events, questions, submissions
                        ▼
┌──────────────────────────────────────────────────────┐
│ FASTAPI BACKEND (VULTR), one application             │
│  Session API · Event ingestion · Command API         │
│              ▼                                       │
│  Training coordinator                                │
│  ├── Rule-based scorer (+ cost estimator)            │
│  ├── Context builder                                 │
│  ├── AI response validator                           │
│  └── Follow-up exercise selector                     │
│       ▼              ▼              ▼                │
│     Gemini        Cartesia     Convex client         │
│    (coaching)     (speech)    (persistence / sync)   │
└────────┬──────────────┬──────────────┬───────────────┘
         ▼              ▼              ▼
    Gemini API     Cartesia API   Convex backend
                                       │ reactive queries
                                       ▼
                        ┌────────────────────────────┐
                        │ INSTRUCTOR WEB APP         │
                        │ React + TypeScript         │
                        │ Live session overview      │
                        │ Simplified 3D view         │
                        │ Annotations / targets      │
                        │ Debrief and progress       │
                        └────────────────────────────┘
Optional later: Tiger Data for high-volume timestamped telemetry.
```

- **XR:** Unity, OpenXR, Meta XR SDK. Controllers first; hand tracking only after the core exercise works.
- **Dashboard:** React + TypeScript (React Three Fiber only if the 3D view needs it).
- **Deployment:** one Vultr VM running backend and dashboard behind HTTPS.
- Convex has a Python client and reactive subscriptions, so the dashboard talks to the Python backend without Unity needing a native Convex integration.

## 1. Runs inside Unity

Unity owns everything that must react immediately.

**Practice scene (v1):** simplified wound-closure practice surface with clearly defined target locations, one or two virtual instruments, sequenced tool-placement actions, highlighted exclusion zones, and a reset/retry button. Tissue deformation, cutting, thread simulation and knot mechanics are **not** prerequisites for the demo.

**Procedure state machine** (explicit steps; the LLM never decides what happens next):

```
READY → SELECT_TOOL → APPROACH_TARGET → COMPLETE_PLACEMENT → CHECK_PLACEMENT
        ├── retry current step
        └── advance to next target → COMPLETE → REVIEW
```

Each state defines allowed interactions, what counts as completion, which events are recorded, and which immediate feedback shows. Exercise definitions are ScriptableObjects or versioned JSON.

**Local feedback engine** (never waits on Gemini, Convex or the backend): exclusion-zone entry → red overlay; placement hits target → completion indicator; wrong tool → corrective UI; step complete → advance state.

**Event recorder:** structured actions, never headset video.

```json
{"eventId":"evt_0042","sessionId":"session_123","attemptId":"attempt_02","sequence":42,
 "elapsedMs":18420,"type":"placement_completed","stepId":"target_03","toolId":"practice_tool",
 "positionLocal":[0.012,0.004,0.031],"targetErrorMm":4.2,"exclusionZoneContact":false}
```

Coordinates are relative to the practice surface, not the headset's world origin. The dashboard uses the same surface model, units and conversion, so the instructor sees placements in the right spot even though the browser and Quest share no physical space.

## 2. Runs on the backend

One FastAPI app with separate modules, not separately deployed services.

- **Session manager:** create sessions, issue scoped trainee/instructor tokens, associate attempts, manage status. A join code is an invitation mechanism, not a replacement for authorization.
- **Event ingestion:** important task events sent immediately; movement samples batched about once per second; full attempt summary at the end; unsent events kept in a local retry buffer. `eventId` + sequence number make retries idempotent.
- **Deterministic scorer:** recomputes metrics from submitted events and a versioned scenario rubric (targets completed, mean target error, exclusion-zone contacts, sequence violations, duration). Any overall score uses a documented prototype rubric. Gemini never invents a score, and nothing is labeled a probability of real surgical success.
- **Cost estimator** (project goal, built on the scorer output): itemized cost of the attempt from sourced rate tables (OR time, consumables, error and rework penalties). Labeled an illustrative estimate, not billing data.
- **Training coordinator:**

```
Attempt submitted → validate events + scenario version → compute metrics
→ build bounded coaching context → structured Gemini request
→ validate references + permitted exercise IDs → save feedback
→ generate spoken version (Cartesia) → return to Quest and instructor
```

The coordinator selects only exercises that already exist. It never generates Unity scenes or executable code.

## 3. AI: measurements first, explanation second

Gemini receives structured evidence, not an open request to "judge the surgery". With one scenario, a short curated explanation of the exercise and rubric goes in the prompt; no vector database or RAG is needed.

Input:
```json
{"scenarioId":"closure_practice_v1","scenarioVersion":1,"currentStep":"review",
 "metrics":{"targetsCompleted":5,"meanTargetErrorMm":4.2,"exclusionZoneContacts":2,"sequenceViolations":0},
 "notableEvents":[{"eventId":"evt_0042","description":"Placement exceeded scenario target tolerance"}],
 "allowedNextExercises":["target_accuracy_easy","target_accuracy_repeat"],
 "traineeQuestion":"What should I focus on next?"}
```

Output (schema-constrained JSON, defined with Pydantic):
```json
{"summary":"You completed the sequence, but target placement was inconsistent.",
 "observations":[{"eventId":"evt_0042","message":"This placement fell outside the marked target tolerance."}],
 "nextExerciseId":"target_accuracy_easy",
 "spokenFeedback":"Focus on target accuracy in the next practice round.",
 "needsInstructorReview":false}
```

A valid schema does not establish factual correctness, so the backend validates event references and exercise IDs itself.

Boundaries: scorer measures, AI explains, instructor reviews, no clinical validation claims. "Adaptive coaching" is the demo claim, not a surgical-success predictor.

## 4. Voice (Cartesia)

First implementation is spoken output only:

```
Attempt or question → backend → Gemini → coaching text → backend → Cartesia → audio → Quest playback
```

Provider-specific audio handling stays inside a backend adapter. Return a short-lived audio URL or audio in a format tested on the headset.

Voice input comes afterward, push-to-talk: Quest mic → short upload → speech-to-text adapter → transcript + exercise context → Gemini → Cartesia → Quest. Speech-to-text is a separate, explicitly chosen and tested service.

Also: visible mic indicator, stop-speaking control, a text version of every spoken response, and a fallback if transcription or speech generation fails. No always-listening voice or simultaneous conversations in the first milestone.

## 5. Instructor collaboration

The instructor sees a synchronized representation of the exercise, not a headset livestream.

- **Quest → instructor:** Quest events → FastAPI → Convex mutation → React subscription updates.
- **Instructor → Quest:** instructor action → authorized Convex mutation creates a command → Quest polls FastAPI → FastAPI reads commands from Convex → Unity applies and acknowledges. Roughly 500–1,000 ms latency is fine; it is not hard real-time. WebSockets only if polling proves to be a problem.

```json
{"commandId":"cmd_018","type":"highlight_target","targetId":"target_03","expiresAt":"2026-10-24T07:00:00Z"}
```

Allowed commands: highlight target, add annotation, request pause, assign an implemented exercise, request retry. Expired commands and commands invalid for the current scenario are rejected. Commands never overwrite tool transforms or bypass the local state machine.

## 6. Data model

Convex is the only application database initially.

| Collection | Important fields |
|---|---|
| sessions | scenario ID/version, trainee, instructor, status, start time |
| attempts | session ID, attempt number, metrics, rubric version, completion status |
| eventBatches | attempt ID, sequence range, bounded event payload |
| feedback | attempt ID, observations, referenced event IDs, next exercise |
| annotations | session ID, author, target ID or local position, message |
| commands | session ID, type, payload, expiry, acknowledgement |

Store sampled or task-relevant events only, not every rendered frame. Tiger Data is added only if detailed trajectory analysis becomes a real feature, with a clear split: Convex for application state and collaboration, Tiger Data for timestamped movement telemetry. Do not duplicate data into both just to claim another integration.

## 7. API surface

| Endpoint | Purpose |
|---|---|
| POST /sessions | Create session |
| POST /sessions/{id}/join | Exchange invitation for scoped access |
| POST /sessions/{id}/events | Upload deduplicated event batch |
| GET /sessions/{id}/commands | Retrieve pending instructor commands |
| POST /sessions/{id}/commands/{commandId}/ack | Acknowledge command |
| POST /attempts/{id}/submit | Finalize metrics, start debrief generation |
| POST /sessions/{id}/coach | Ask a contextual coaching question |
| GET /jobs/{id} | Coaching/debrief status |
| POST /sessions/{id}/audio | Optional recorded voice question |
| GET /health | Backend availability |

Slow coaching work returns a job ID so the Quest keeps rendering. A single-process worker with persisted job status is enough; after a restart, interrupted jobs are marked retryable rather than stuck as "running".

## 8. Reliability and security

| Problem | Required behavior |
|---|---|
| Internet disconnects | Continue local practice; buffer events |
| Gemini times out | Show metrics and deterministic feedback |
| Cartesia fails | Show text feedback |
| Duplicate upload | Dedupe by eventId and sequence |
| Backend restarts mid-job | Mark job retryable |
| Annotation refers to unknown target | Reject it |

- Keep Gemini, Cartesia and database credentials off the headset and out browser bundles.
- Enforce session-level authorization in both FastAPI and Convex functions.
- Rate-limit coaching requests; use HTTPS.
- Use synthetic practice cases, never patient data.
- Do not retain raw microphone recordings by default.
- Never execute code returned by AI.

## 9. Milestones

1. **Offline practice attempt:** Quest build installs, user grabs a tool, one target interaction works, local feedback appears, attempt metrics display.
2. **Backend round trip:** create session, upload one event batch, submit attempt, receive deterministic metrics.
3. **Contextual coaching:** metrics to Gemini, validated structured feedback shown in Unity, Cartesia playback.
4. **Human collaboration:** instructor joins from the browser, dashboard receives attempt data, instructor highlights a target, Quest applies and acknowledges it.
5. **Adaptive retry:** coach selects two or three predefined exercises, user completes a retry, dashboard compares attempts.

Only then: voice input, replay, richer analytics, additional sponsor integrations.

## Open items

- The existing `backend/` implements a peg-transfer session API (`/api/v1/sessions/...`). The plan above uses a wound-closure scenario with scenario-versioned events and different endpoints. Decide whether to keep peg transfer as the first scenario or migrate; the scorer and dedupe logic carry over either way.
- Cost estimator rates are sourced in [COST_DATA.md](COST_DATA.md); the per-error adverse-event probability is an explicit assumption.
