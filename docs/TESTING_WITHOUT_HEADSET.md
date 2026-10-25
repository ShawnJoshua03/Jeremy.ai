# Testing without a headset

Layers for the laparoscopic cholecystectomy Critical View of Safety trainer (`lap_chole_cvs`). Contract: [CONTRACT_CVS.md](CONTRACT_CVS.md).

This page was written 2026-10-24 by reading the tree. Nothing in it was executed. Each layer below is **not verified**.

Default tests mock Claude, Cartesia, Apify, and x402. They need no network and no keys. Live provider checks are opt-in and stay out of `pytest -q`.

## 1. Golden session replays

`backend/tests/fixtures/cvs_*.json` is the lock for v1. A replay posts one saved session (create, start, samples, events, complete) and checks the deterministic score, grade, per-exercise correctness, movement totals, and mistake-cost numbers. Claude prose is mocked. A renamed field or a renumbered score fails here.

The starter exercises are one fixture. A second fixture sends an unknown exercise id, an unknown structure id, and the reserved kinds `step_completed`, `clip_applied`, `structure_cut`, `injury`, and `critical_view`. The report lists the unknown exercise with `correct: null`. Replay and the report still contain those events. The session does not 4xx them. The score does not treat them as wrong. The fixture set fails if a test switches on the three starter ids and drops anything else.

The `backend/tests/fixtures/` directory is not in this tree. There is no `cvs_*.json` file and no replay test for `lap_chole_cvs`.

When the fixtures exist, from `backend/`:

```powershell
.\.venv\Scripts\python -m pytest -q
```

Report failures only. This docs pass did not run pytest.

## 2. Schema snapshot

A checked-in snapshot of the v1 JSON shapes sits beside the golden fixtures: session create, sample (including `instrument: "head"`), each starter event kind, the reserved procedure kinds, `GET /sessions/{sid}/report` (including `correct: null`), and `GET /sessions/{sid}/replay`. The test fails if a field is renamed, removed, or changes type. Adding a field updates the snapshot in the same commit as [CONTRACT_CVS.md](CONTRACT_CVS.md). Adding an exercise is a table row and a fixture, not a schema break.

No snapshot file is in this tree. **Not verified.**

## 3. Unity DesktopPointer against a local backend

The scene file is `quest/Assets/GallbladderTrainer/Scenes/GallbladderTraining.unity`. `DesktopPointer` raycasts structures with the mouse. Answer buttons are world-space UI. Play mode was not run.

Start the API from `backend/`:

```powershell
.\.venv\Scripts\python -m uvicorn main:app --reload
```

Interactive docs: `http://127.0.0.1:8000/docs`. Optional auth is the `X-API-Key` header when `API_KEY` is set.

In Unity 6, open the `quest` project, open `Assets/GallbladderTrainer/Scenes/GallbladderTraining.unity`, and press Play. The game view uses `Desktop Test Camera`. This scene is the starter view. Complete its three exercises with the mouse:

1. Click **Assess**. Answers stay disabled in Explore. The scene starts in Explore.
2. Exercise 1, identify the cystic duct. Click the narrow tube that leaves the gallbladder neck, then click **Submit selected structure**.
3. Click **Next exercise >**.
4. Exercise 2, prepared view A. Click **Criteria demonstrated in this prepared exercise**. That is the deliberate mistake. The exercise expects `criteria_not_demonstrated`. The contract says this decision is `severity: critical`.
5. Click **Next exercise >**.
6. Exercise 3, prepared view B. Click **Criteria demonstrated in this prepared exercise**. The exercise expects that decision.
7. After complete, `GET /api/v1/sessions/{id}/report`.

`GallbladderTrainer` does not open a socket. `ITrainingBackendHook` is optional, and no component posts samples or events (`TrainingModels.cs`: no networking in this package). Pressing Play does not create a session id, so step 7 has nothing to fetch until a hook implements [CONTRACT_CVS.md](CONTRACT_CVS.md). The backend session exercise literal is still `peg_transfer`, `wound_closure`, `lap_chole`, and `port_placement`. Sample `instrument` is still `left` or `right`. **Not verified.**

## 4. Dashboard demo mode

From `dashboard/`:

```powershell
npm install
npm run dev
```

Open the URL Vite prints. The CORS example in `.env.example` is `http://localhost:5173`. In the sidebar, click **Sample preview**. The empty state has the same control, labeled **Open sample preview**. The header says Preview sessions. Each card is marked Sample preview. The rows are synthetic and come from `dashboard/src/demoData.ts` (peg transfer). They are not a `lap_chole_cvs` report.

This pass did not start Vite. **Not verified.**

## 5. Live provider smoke (opt-in, keys only)

Copy `.env.example` to `.env`. Do not commit `.env`. Skip a check when its key is empty. Do not put these calls in the default pytest run.

| Check | Key | What a pass looks like |
|---|---|---|
| Claude | `ANTHROPIC_API_KEY` (`ANTHROPIC_MODEL` optional) | A narrator or report call whose `source` is `claude`. The text contains only numbers the backend computed. An empty key stays on `source: fallback` |
| Cartesia | `CARTESIA_API_KEY` | `POST /api/v1/sessions/{id}/narrator/line/audio`, and once it exists `POST /api/v1/sessions/{id}/report/audio`, returns `audio/wav`. HTTP 503 means show the text |
| x402 testnet | `X402_PAY_TO`, `X402_FACILITATOR_URL` | Base Sepolia, scheme `exact`. `GET /api/v1/sessions/{id}/mistake-cost/summary` is free. `GET .../mistake-cost/report` is 402 until a testnet payment is verified. The contract also puts itemized `mistake_cost` on `GET /sessions/{id}/report` |

A funded testnet wallet has not been used. Apify (`APIFY_TOKEN`, `APIFY_PRICING_ACTOR`) is the same rule: offline snapshot unless someone runs a refresh and records it. **Not verified.**
