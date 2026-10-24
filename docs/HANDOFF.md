# Handoff / transition doc

Snapshot as of 2026-10-24. Read this first, then [ARCHITECTURE.md](ARCHITECTURE.md). Agents are run through Orca; see "Suggested agent split" for who owns what, and keep token use low.

## What this is

**Jeremy.ai** for CalHacks 13. A trainee practices a focused surgical task on a Meta Quest 3 in passthrough, gets AI voice coaching, and can invite an instructor to watch and annotate from a browser. At the end the app prices the procedure with an itemized, source-cited cost breakdown.

Repo: https://github.com/ShawnJoshua03/Jeremy.ai (private). Collaborators with push access: SpectralGT, msde-7, christinec04 (all accepted).

## Decisions made

| Decision | Detail |
|---|---|
| Stack | Unity local sim + one FastAPI backend (Vultr) + Gemini + Cartesia + Convex + React instructor dashboard. Tiger Data dropped for now (plain SQLite/Postgres). Backend DB is the source of truth; Convex is a live read-only mirror for the dashboard. |
| Claude | **Removed.** Gemini does all AI feedback. |
| Moss | **Removed.** Replaced by Cartesia (text-to-speech). No RAG; one scenario fits in the prompt. |
| Cost | Must use real published data. See [COST_DATA.md](COST_DATA.md). Per-error adverse-event probability is the one explicit assumption. |
| Scoring | Deterministic, rule-based. AI never invents scores. No clinical-validity claims. |
| Offline | Quest simulation must work without internet; events buffer and retry. |
| Possible extra tracks | Apify (Best Actor) and Elastic (Agent Builder) were in the sponsor screenshots. Not started; Elastic requirements unconfirmed. |

## Current state

**Done and pushed**
- `README.md`: stack and 10-item task list (all unchecked).
- `docs/ARCHITECTURE.md`: full architecture plan.
- `docs/COST_DATA.md`: sourced cost rates and the mapping from performance to dollars.
- `docs/UNITY_REFERENCES.md`: reference repos for Quest and surgical training.
- `backend/cost.py` + `GET /api/v1/sessions/{id}/cost`: itemized cost from sourced rates (tests in `tests/test_cost.py`).
- `backend/`: `scoring.py` (deterministic metrics), `database.py` (SQLAlchemy, SQLite locally, `DATABASE_URL` for Postgres/Tiger Data), `main.py` (FastAPI session API under `/api/v1`), `coach.py` (fallback plus validated Gemini; live Gemini **not verified**), `speech/` (Cartesia adapter with text fallback; live Cartesia **not verified**), `mentorship/` (SQLite commands/annotations mirrored to Convex), `convex_mirror.py` (optional push). `tests/` (101 passing).
- `.env.example`, `AGENTS.md`, and the judging docs: `docs/SPONSOR_EVIDENCE.md`, `docs/DEMO_SCRIPT.md`, `docs/KNOWN_LIMITATIONS.md`. Cost citations checked in `docs/COST_DATA.md`.

**In the repo but not verified live**
- Cartesia speech, Convex mirroring, the React dashboard, Unity scripts, and Vultr deployment files all have code in the repo (see KNOWN_LIMITATIONS.md), but none of the live integrations has been run end to end on hardware.

## Open conflict to resolve

The existing backend is a **peg-transfer** API (`/api/v1/sessions/...`, samples + events). `ARCHITECTURE.md` describes a **wound-closure** scenario with scenario-versioned events and different endpoints (`/attempts/{id}/submit`, `/commands`, `/jobs`). Decide: keep peg transfer as scenario 1 (faster, scorer carries over) or migrate. Recommendation: keep peg transfer for the demo and add the new endpoints around it.

## Tooling on this machine

| Tool | Status |
|---|---|
| Unity | **Unity 6.6 (6000.6.7f1). This is the decided version; do not downgrade.** |
| Docker | **Not installed** (deploy and container tests blocked). |
| Python | 3.13; backend venv at `backend/.venv` (not committed). |
| Node | Installed. |
| gh | Installed, authenticated as ShawnJoshua03. |
| Agent CLIs | claude, codex, copilot, opencode (npm global); **grok 1.0.70** at `~/.grok/bin`, signed in and verified. Orca installed at `%LOCALAPPDATA%\Programs\orca\Orca.exe` (not on PATH). |

## Unity project instructions (Unity 6.6, Quest 3)

Setup follows Meta's [manual hello-world steps](https://developers.meta.com/horizon/documentation/unity/unity-tutorial-hello-world-manual-steps/) as fetched on 2026-10-24. Requirements there: Unity 6.1+, **Unity OpenXR Plugin** (`com.unity.xr.openxr`, 1.15.1 recommended, needs Meta XR SDK v74+), and the Meta XR All-in-One SDK (`com.meta.xr.sdk.all`). The Oculus XR Plugin is deprecated; do not use it.

**Version is fixed at Unity 6.6 (6000.6.7f1).** If the Package Manager or Project Setup Tool reports errors, resolve them on 6.6 by updating the Meta XR SDK and Unity packages to their latest versions and re-running Fix All. Record any workarounds in this doc.

### Steps

1. **Unity Hub > Installs > 6000.6.7f1 > Manage > Add modules > Android Build Support** (including OpenJDK and Android SDK & NDK Tools).
2. **Create the project** with the **Universal 3D** template (URP) at `C:/Users/shawn/Jeremy.ai/quest`. The repo's `quest/` folder is the Unity project root.
3. **Install OpenXR:** Edit > Project Settings > XR Plug-in Management > install if prompted, then enable **OpenXR** for both the Standalone and Android tabs.
4. **Switch platform:** File > Build Profiles > **Meta Quest** > Switch Platform (install `com.unity.xr.openxr` if prompted).
5. **Add the SDK:** Window > Package Management > Package Manager > **+ > Add package by name** > `com.meta.xr.sdk.all`. Accept the Meta XR feature-set prompt; restart the editor if asked.
6. **Project Setup Tool:** Window > Meta > Tools > Project Setup Tool. For **both Standalone and Android**, press **Fix All** on Outstanding Issues, review Recommended Items, fix Manually Fixable Items, and confirm no required issues remain. This tool applies the graphics API, scripting backend and architecture settings, so do not hand-set them.
7. **Update packages** in the Package Manager Updates tab.
8. **Verify features:** Project Settings > XR Plug-in Management > OpenXR > Android tab, filter "Meta XR", confirm **Meta XR Feature** is enabled (foveation default on).
9. **Passthrough:** use Meta Building Blocks (Window > Meta > Tools > Building Blocks): add **Camera Rig**, **Passthrough**, **Controller Tracking**, and **Grab Interaction**. Follow Meta's [passthrough tutorial](https://developers.meta.com/vr/documentation/unity/unity-passthrough-tutorial-with-blocks/). Keep an opaque VR fallback toggle (see Meta's Starter Samples passthrough scene).
10. **Headset:** enable Developer Mode on the Quest 3 (Meta Horizon phone app), connect over USB, accept the debugging prompt, and select it as the run device in Build Profiles. Verify with `adb devices` (status must read `device`). `adb install -r <apk>` to sideload.
11. **Commit hygiene:** `.gitignore` already excludes Library, Temp, Logs, UserSettings and Obj. Commit `Assets`, `Packages` and `ProjectSettings`. Add Git LFS rules for large binary assets if the repo grows.

Optional: create an app on the Meta Developer Dashboard and add its App ID only if a Platform feature (such as entitlements) is needed. Not required for the demo.

### Scene and scripts to build (matches ARCHITECTURE.md)

Folders under `quest/Assets/Scripts/`: `Core`, `Exercises`, `Interaction`, `Tracking`, `Networking`, `Feedback`, `Mentorship`.

| Script | Responsibility |
|---|---|
| `ExerciseStateMachine` | Explicit states: Idle, Ready, Running, Paused, Completed, Cancelled. Timer starts once. No internet dependency. |
| `PegBoard` | Flat board about 0.5 m in front of the user, 6 source and 6 target positions, recenter and manual-reposition fallback. |
| `InstrumentController` | One virtual instrument per controller, tip offset from the controller pose, trigger closes the grasper and attaches a valid object in range, release drops it. Highlight objects in range. |
| `TransferDetector` | Success when released inside the matching target; drop or invalid release otherwise; an object scores only once; records which controller did it. |
| `EventRecorder` | Structured events with `eventId`, `sequence`, `elapsedMs`, positions in the **board's local frame**. |
| `PoseSampler` | Tip pose at 20-30 Hz, flags invalid tracking so gaps are never treated as motion. |
| `ApiClient` + `UploadQueue` | HTTPS to FastAPI; events immediately, samples about once per second; bounded queue with retry; idempotent via IDs. |
| `HudController` | Timer, progress, error indicator, reset button, results panel (metrics and the itemized cost breakdown from `/cost`). |
| `CoachPanel` + `CoachAudioPlayer` | Show Gemini text and play Cartesia audio, stop-speaking control, text fallback. |
| `CommandPoller` | Poll `/commands`, apply highlight/annotation/pause/retry, ack. Never overwrite tool transforms. |

Controller input: use the Meta Interaction SDK / OpenXR input actions as shown in the installed SDK's own samples, and confirm API names against the installed package instead of writing them from memory.

### Starting points

See [UNITY_REFERENCES.md](UNITY_REFERENCES.md): Meta Starter Samples (passthrough), Interaction SDK samples (MR Tabletop, controller grab), XR-CV-Forceps-Tracking-Unity (surgical grasping), LapSim-AI (peg transfer logic).

### Not verified yet

Nothing in this section has been run. No Unity project exists yet, no Quest build has been made, and the Meta XR SDK has not yet been installed on 6.6.

## Run it

```powershell
cd C:\Users\shawn\Jeremy.ai\backend
.\.venv\Scripts\python -m pytest -q                      # 101 tests
.\.venv\Scripts\python -m uvicorn main:app --reload      # API on :8000, docs at /docs
```
Recreate the venv if missing: `python -m venv .venv; .\.venv\Scripts\pip install -r requirements.txt`.

## Next steps (in order)

1. ~~Cost module~~ done.
2. **Unity project** in `quest/`: follow the Unity section above.
3. **Backend round trip** from Quest: session, event batch, submit, metrics and cost.
4. **Gemini coaching** — endpoint and fallback are in `backend/coach.py`. Live model name and request format are still **not verified**.
5. **Cartesia speech** adapter, with text fallback.
6. **Convex + React dashboard**: live session, replay with highlighted errors, annotations and commands to the headset.
7. **Deploy to Vultr** (needs Docker or a plain systemd/uvicorn setup).
8. ~~Judging docs~~ drafted: SPONSOR_EVIDENCE.md, DEMO_SCRIPT.md, KNOWN_LIMITATIONS.md, AGENTS.md. Do not claim sponsor eligibility or integration success that was not verified.

## Suggested agent split

**Run every agent through Orca** (`%LOCALAPPDATA%\Programs\orca\Orca.exe`), one agent per git worktree/branch so they never edit the same files. Merge to `main` through PRs.

| Agent | Owns (paths) | Tasks (next-step #) | Why this agent |
|---|---|---|---|
| claude | `backend/` | #3 round-trip endpoints, #4 Gemini coaching (Pydantic schema, event-ID validation), peg-transfer vs wound-closure decision | Cross-file reasoning, schema/validation correctness |
| codex (low quota, handing over to grok) | `quest/Assets/Scripts/` | #2 Unity project and all C# in the script table (state machine, PegBoard, InstrumentController, TransferDetector, EventRecorder, PoseSampler, ApiClient/UploadQueue) | Bulk code generation from a precise spec |
| copilot | `dashboard/`, `convex/` | #6 React live view, replay, annotations, commands | Well-trodden React/TS boilerplate |
| opencode | `backend/speech/`, `deploy/` | #5 Cartesia adapter with text fallback, #7 Vultr systemd/uvicorn deploy scripts | Self-contained, small-surface adapters |
| grok | `docs/`, `backend/tests/` | #8 SPONSOR_EVIDENCE, DEMO_SCRIPT, KNOWN_LIMITATIONS, extra tests, `.env.example`, AGENTS.md | Writing and verification; must not claim unverified integrations |

Human-only: Unity editor steps (modules, Project Setup Tool, Fix All), Quest Developer Mode, `adb`, API keys and account signups.

### Workflow: Claude last, others do and review

Claude has the smallest budget, so it only coordinates and handles escalations.
1. Work goes to the agents in the table above, never to Claude by default.
2. Every agent branch is reviewed by a **different** agent before merge: grok reviews codex; copilot reviews grok. Codex's quota is low, so grok inherits the Unity C# and mentorship endpoints and Claude reviews grok only on escalation. The reviewer checks that tests pass, the diff stays in the owner's paths, and no unverified claims were made, then replies APPROVE or a short list of fixes.
3. If the owner fixes the issues and the second review still fails, or the change touches `backend/` scoring, cost or the API contract, **then** Claude reviews. Claude does not write features unless all other agents are blocked.
4. Merges to `main` go through the reviewing step, then `pytest -q` must pass before push.

### Token rules for every agent

- Start by reading only this doc and the one file or section you own; do not load the whole repo. Use `Grep`/outline before opening large files and read line ranges.
- Put shared context in `AGENTS.md` (to write) once, so prompts stay short: "do task #N, paths X, done when tests Y pass".
- One task per session; commit, then start a fresh session instead of growing a long context. Hand off via a short note in this file, not a transcript.
- Do not paste long logs or docs into prompts; give a path and a line range. Run tests with `-q` and show only failures.
- Do not re-fetch docs already summarized in `docs/` (COST_DATA, UNITY_REFERENCES, ARCHITECTURE).
- Use the cheapest capable model per task (small model for boilerplate, adapters, docs; the large model only for backend design and debugging).

## Credentials needed (none committed)

`GEMINI_API_KEY`, `CARTESIA_API_KEY`, Convex deployment URL/key, Vultr account, optional Tiger Data `DATABASE_URL`. Keep all keys off the headset and out of the browser bundle. `.env` is git-ignored.

## Caveats

- Cost citations were opened on 2026-10-24. Figures the sources do not support are listed in `docs/COST_DATA.md` (Source check).
- Nothing has been run against Tiger Data, Gemini, Cartesia, Convex or Vultr yet.
- No Quest hardware testing has happened.
